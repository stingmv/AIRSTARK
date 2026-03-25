using DICOMViews;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Threads;
using UnityEngine;

using DICOMViews.Loaders;
using DICOMViews.Strategies;

using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace DICOMParser
{

    /// <summary>
    /// Contains all DICOM data and data generated from it.
    /// </summary>
    public class ImageStack : MonoBehaviour
    {

        private int[] _data;//almacenara valores de intensidad de volumen 3D->1D

        public ComputeShader DicomComputeShader;
        private Texture2D[] _transversalTexture2Ds;
        private Texture2D[] _frontalTexture2Ds;
        private Texture2D[] _sagittalTexture2Ds;
        
        private DiFile[] _dicomFiles;

        public Text DebugText;//para mostrar mensajes
        public Texture3D VolumeTexture { get; private set; }//propiedad publica de solo lectura externa que guarda el Texture3D creado para renderizado de volumen

        private string _folderPath;//ruta de la carpeta actual, en privado

        public double WindowCenter { get; set; } = double.MinValue;
        public double WindowWidth { get; set; } = double.MinValue;

        //arrays con presets tomados del DICOM
        public double[] WindowCenterPresets { get; private set; }
        public double[] WindowWidthPresets { get; private set; }

        public int MinPixelIntensity { get; set; }
        public int MaxPixelIntensity { get; set; }

        //Dimensiones y acceso a slices
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Slices => DicomFiles?.Length ?? 0;

        public DiFile[] DicomFiles {get; private set; }

        public TextureUpdate OnTextureUpdate = new TextureUpdate();

        /// <summary>
        /// Start coroutine for parsing of files.
        /// </summary>
        public async Task StartParsingFiles(string folderPath, ThreadGroupState state)
        {
            Debug.Log("START PARSING FILES");
            await InitFiles(folderPath, state);
        }

        /// <summary>
        /// Starts coroutine for preprocessing DICOM pixeldata
        /// </summary>
        public async Task StartPreprocessData(ThreadGroupState state)//inicializa el preprocessing de pixeles
        {
            state.TotalProgress = DicomFiles.Length;//Inicializa threadGroupstate.totalprogress con numero de slices
            await PreprocessData(state);//llama metodo, internamente arrancara hilos
        }

        /// <summary>
        /// Starts coroutine for creating the 3D texture
        /// </summary>
        public async Task StartCreatingVolume(ThreadGroupState state)
        {
            state.TotalProgress = DicomFiles.Length;
            await CreateVolume(state);
        }

        /// <summary>
        /// Start coroutine for creating 2D textures.
        /// </summary>
        public async Task StartCreatingTextures(ThreadGroupState state)
        {
            state.TotalProgress = DicomFiles.Length + Width + Height;
            await CreateTextures(state);
        }


        /// <summary>
        /// Allows a Unity coroutine to wait for every working thread to finish.
        /// </summary>
        /// <param name="threadGroupState">Thread safe thread-state used to observe progress of one or multiple threads.</param>
        /// <returns>IEnumerator for usage as a coroutine</returns>


        private async Task InitFiles(string folderPath, ThreadGroupState threadGroupState)
        {
            Debug.Log("INIT FILES");
            threadGroupState.Register();
            
            var loader = DICOMViews.Loaders.DicomLoaderFactory.CreateLoader();
            DicomFiles = await loader.LoadFilesAsync(folderPath, threadGroupState);

            if (DicomFiles.Length == 0)
            {
                Debug.LogError("No DICOM files were loaded.");
                threadGroupState.Done();
                return;
            }

            Width = DicomFiles[0].GetImageWidth();
            Height = DicomFiles[0].GetImageHeight();
            _data = new int[DicomFiles.Length * Width * Height];

            VolumeTexture = null;

            WindowCenterPresets = DicomFiles[0].GetElement(0x0028, 0x1050)?.GetDoubles() ?? new[] { double.MinValue };
            WindowWidthPresets = DicomFiles[0].GetElement(0x0028, 0x1051)?.GetDoubles() ?? new[] { double.MinValue };
            WindowCenter = WindowCenterPresets[0];
            WindowWidth = WindowWidthPresets[0];

            MinPixelIntensity = (int)(DicomFiles[0].GetElement(0x0028, 0x1052)?.GetDouble() ?? 0d);
            MaxPixelIntensity = (int)((DicomFiles[0].GetElement(0x0028, 0x1053)?.GetDouble() ?? 1d) * (System.Math.Pow(2, DicomFiles[0].GetBitsStored()) - 1) + MinPixelIntensity);

            threadGroupState.Done();
        }

       


        /// <summary>
        /// Window Settings have been changed.
        /// </summary>
        /// <param name="windowWidth">new window width</param>
        /// <param name="windowCenter">new window center</param>
        public void OnWindowSettingsChanged(double windowWidth, double windowCenter)
        {
            WindowWidth = windowWidth;
            WindowCenter = windowCenter;
        }



        private async Task PreprocessData(ThreadGroupState threadGroupState)
        {
            await StartPreProcessing(threadGroupState, DicomFiles, _data, 12);
        }

        /// <summary>
        /// Unity coroutine used to create the 3D texture using multiple threads.
        /// </summary>
        /// <param name="threadGroupState">Thread safe thread-state used to observe progress of one or multiple threads.</param>
        /// <returns>IEnumerator for usage as a coroutine</returns>
        private async Task CreateVolume(ThreadGroupState threadGroupState)
        {
            var builder = new DICOMViews.Builders.VolumeTextureBuilder(DicomComputeShader, DicomFiles, _data, Width, Height, WindowWidth, WindowCenter);
            VolumeTexture = await builder.BuildVolumeAsync(threadGroupState);
        }

        /// <summary>
        /// Unity coroutine used to create all textures using multiple threads.
        /// </summary>
        /// <param name="threadGroupState">Thread safe thread-state used to observe progress of one or multiple threads.</param>
        /// <returns>IEnumerator for usage as a coroutine</returns>
        private async Task CreateTextures(ThreadGroupState threadGroupState)
        {
#if PRINT_USAGE
            Debug.Log(Time.time +
                      $" : Started Creating Textures with Window (Center {WindowCenter}, Width {WindowWidth})");
#endif

            _transversalTexture2Ds = new Texture2D[DicomFiles.Length];
            _frontalTexture2Ds = new Texture2D[Height];
            _sagittalTexture2Ds = new Texture2D[Width];

            var transTextureColors = new Color32[DicomFiles.Length][];
            var frontTextureColors = new Color32[Height][];
            var sagTextureColors = new Color32[Width][];

            var transProgress = new ConcurrentQueue<int>();
            var frontProgress = new ConcurrentQueue<int>();
            var sagProgress = new ConcurrentQueue<int>();

            var t1 = StartCreatingSlices(threadGroupState, transProgress, SliceType.Transversal, _data, DicomFiles, transTextureColors, WindowWidth, WindowCenter, 2);
            var t2 = StartCreatingSlices(threadGroupState, frontProgress, SliceType.Frontal, _data, DicomFiles, frontTextureColors, WindowWidth, WindowCenter, 2);
            var t3 = StartCreatingSlices(threadGroupState, sagProgress, SliceType.Sagittal, _data, DicomFiles, sagTextureColors, WindowWidth, WindowCenter, 2);

            while (threadGroupState.Working > 0 || !(transProgress.IsEmpty && frontProgress.IsEmpty && sagProgress.IsEmpty))
            {
                int current;
                if (transProgress.TryDequeue(out current))
                {
                    CreateTexture2D(Width, Height, transTextureColors, _transversalTexture2Ds, current);
                    OnTextureUpdate.Invoke(SliceType.Transversal, current);
                    threadGroupState.IncrementProgress();
                }

                if (frontProgress.TryDequeue(out current))
                {
                    CreateTexture2D(Width, DicomFiles.Length, frontTextureColors, _frontalTexture2Ds, current);
                    OnTextureUpdate.Invoke(SliceType.Frontal, current);
                    threadGroupState.IncrementProgress();
                }

                if (sagProgress.TryDequeue(out current))
                {
                    CreateTexture2D(Height, DicomFiles.Length, sagTextureColors, _sagittalTexture2Ds, current);
                    OnTextureUpdate.Invoke(SliceType.Sagittal, current);
                    threadGroupState.IncrementProgress();
                }

                await Task.Yield();
            }
            
            await Task.WhenAll(t1, t2, t3);
        }

        /// <summary>
        /// Creates a new Texture 2D with given width, height and Colors and writes it to the target Texture2D array. Also notifies the viewmanager of the texture update.
        /// </summary>
        /// <param name="width">Width of the to be created texture.</param>
        /// <param name="height">Height of the to be created texture.</param>
        /// <param name="textureColors">Array of all colors for every slice of this slice type.</param>
        /// <param name="target">Target array to write the texture2D to.</param>
        /// <param name="current">Index of the Texture to be created.</param>
        private static void CreateTexture2D(int width, int height, IReadOnlyList<Color32[]> textureColors, IList<Texture2D> target, int current)
        {
            var currentTexture2D = new Texture2D(width, height, TextureFormat.ARGB32, true);
            currentTexture2D.SetPixels32(textureColors[current]);
            currentTexture2D.filterMode = FilterMode.Point;
            currentTexture2D.Apply();
            Destroy(target[current]);
            target[current] = currentTexture2D;
        }

        /// <summary>
        /// Checks if a file has an extension.
        /// </summary>
        /// <param name="f">the filename</param>
        /// <returns>true if the file has no extension.</returns>
        private static bool HasNoExtension(string f)
        {
            return !Regex.Match(f, @"[.]*\.[.]*").Success;
        }

        /// <summary>
        /// Returns the raw data array containing the intensity values.
        /// </summary>
        /// <returns>The raw 3D array with intensity values.</returns>
        public int[] GetData()
        {
            return _data;
        }

        /// <summary>
        /// Returns the Texture2D with the given index of the given SliceType.
        /// </summary>
        /// <param name="type">Requested SliceType</param>
        /// <param name="index">The index of the texture</param>
        /// <returns></returns>
        public Texture2D GetTexture2D(SliceType type, int index)
        {
            switch (type)
            {
                case SliceType.Transversal:
                    
                    if (_transversalTexture2Ds != null && index < _transversalTexture2Ds.Length)
                    {
                        return _transversalTexture2Ds[index];
                    }
                    else
                    {
                        return null;
                    }
                    
                case SliceType.Frontal:
                    if (_frontalTexture2Ds != null && index < _frontalTexture2Ds.Length)
                    {
                        return _frontalTexture2Ds[index];
                    }
                    else
                    {
                        return null;
                    };
                case SliceType.Sagittal:
                    if (_sagittalTexture2Ds != null && index < _sagittalTexture2Ds.Length)
                    {
                        return _sagittalTexture2Ds[index];
                    }
                    else
                    {
                        return null;
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        /// <summary>
        /// Checks if the arrays containing the slices exist.
        /// </summary>
        /// <param name="type">Requested SliceType</param>
        /// <returns>True if the corresponding array is not null</returns>
        public bool HasData(SliceType type)
        {
            switch (type)
            {
                case SliceType.Transversal:
                    return _transversalTexture2Ds != null;
                case SliceType.Frontal:
                    return _frontalTexture2Ds != null;
                case SliceType.Sagittal:
                    return _sagittalTexture2Ds != null;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns the maximum value for the given slice type, starting from 0.
        /// </summary>
        /// <param name="type">Requested SliceType</param>
        /// <returns>Max Value for the SliceType</returns>
        public int GetMaxValue(SliceType type)
        {
            switch (type)
            {
                case SliceType.Transversal: return DicomFiles.Length - 1;
                case SliceType.Frontal: return Height-1;
                case SliceType.Sagittal: return Width-1;
                default: return 0;
            }
        }

        /// <summary>
        /// Starts one or more Threads for preprocessing.
        /// </summary>
        /// <param name="groupState">synchronized Threadstate used to observe progress of one or multiple threads.</param>
        /// <param name="files">all the DICOM files.</param>
        /// <param name="target">1D array receiving the 3D data.</param>
        /// <param name="threadCount">Amount of Threads to use.</param>
        private async Task StartPreProcessing(ThreadGroupState groupState, IReadOnlyList<DiFile> files, int[] target, int threadCount)
        {
            int spacing = files.Count / threadCount;
            var tasks = new System.Collections.Generic.List<Task>();

            for (var i = 0; i < threadCount; ++i)
            {
                var startIndex = i * spacing;
                var endIndex = startIndex + spacing;

                if (i + 1 == threadCount)
                {
                    endIndex = files.Count;
                }

                groupState.Register();
                tasks.Add(Task.Run(() => PreProcess(groupState, files, Width, Height, target, startIndex, endIndex)));
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Fills the target array with 3D data while applying basic preprocessing.
        /// </summary>
        /// <param name="groupState">synchronized Threadstate used to observe progress of one or multiple threads.</param>
        /// <param name="files">all the DICOM files.</param>
        /// <param name="width">width of a DICOM slice.</param>
        /// <param name="height">height of a DICOM slice.</param>
        /// <param name="target">1D array receiving the 3D data.</param>
        /// <param name="start">Start index used to determine partition of images to be computed</param>
        /// <param name="end">End index used to determine upper bound of partition of images to be computed</param>
        private static void PreProcess(ThreadGroupState groupState, IReadOnlyList<DiFile> files, int width, int height,
            IList<int> target, int start, int end)
        {
            var storedBytes = new byte[4];           

            for (var layer = start; layer < end; ++layer)
            {
                var currentDiFile = files[layer];
                var pixelData = currentDiFile.RemoveElement(0x7FE0, 0x0010);
                uint mask = ~(0xFFFFFFFF << (currentDiFile.GetHighBit()+1));
                int allocated = currentDiFile.GetBitsAllocated() / 8;

                var baseOffset = layer * width * height;

                using (var pixels = new MemoryStream(pixelData.GetValues()))
                {
                    for (var y = 0; y < height; ++y)
                    {
                        for (var x = 0; x < width; ++x)
                        {
                            //get current Int value
                            pixels.Read(storedBytes, 0, allocated);
                            var value = BitConverter.ToInt32(storedBytes, 0);
                            var currentPix = GetPixelIntensity((int)(value & mask), currentDiFile);
                            target[baseOffset + y * width + x] = currentPix;
                        }
                    }

                }

                groupState.IncrementProgress();
                Thread.Sleep(10);
            }
         
            groupState.Done();
        }


        private async Task StartCreatingSlices(ThreadGroupState groupState, ConcurrentQueue<int> processed, SliceType type, int[] data, IReadOnlyList<DiFile> files, IList<Color32[]> target, double windowWidth, double windowCenter, int threadCount)
        {
            var strategy = SliceStrategyFactory.GetStrategy(type);
            int maxIndex = strategy.GetMaxIndex(Width, Height, files.Count);
            int spacing = maxIndex / threadCount;
            var tasks = new System.Collections.Generic.List<Task>();

            for (var i = 0; i < threadCount; ++i)
            {
                groupState.Register();
                var startIndex = i * spacing;
                var endIndex = (i + 1 == threadCount) ? maxIndex : startIndex + spacing;

                tasks.Add(Task.Run(() => CreateSlices(strategy, groupState, processed, data, Width, Height, files, target, windowWidth, windowCenter, startIndex, endIndex)));
            }
            await Task.WhenAll(tasks);
        }

        private static void CreateSlices(ISliceStrategy strategy, ThreadGroupState groupState, ConcurrentQueue<int> processed, int[] data, int width, int height, IReadOnlyList<DiFile> files, IList<Color32[]> target, double windowWidth, double windowCenter, int start, int end)
        {
            int targetWidth = strategy.GetTargetWidth(width, height, files.Count);
            int targetHeight = strategy.GetTargetHeight(width, height, files.Count);

            for (var i = start; i < end; ++i)
            {
                target[i] = new Color32[targetWidth * targetHeight];
                strategy.FillPixels(i, data, width, height, files, target[i], TransferFunction.Identity, windowWidth, windowCenter);
                processed.Enqueue(i);
                Thread.Sleep(5);
            }
            groupState.Done();
        }

        /// <summary>
        /// Applies intercept and slope of the given DiFile.
        /// </summary>
        /// <param name="rawIntensity">Raw pixel intensity</param>
        /// <param name="file">DiFile containing the pixel</param>
        /// <returns>The resulting value</returns>
        private static int GetPixelIntensity(int rawIntensity, DiFile file)
        {
            var interceptElement = file.GetElement(0x0028, 0x1052);
            var slopeElement = file.GetElement(0x0028, 0x1053);

            var intercept = interceptElement?.GetDouble() ?? 0;
            var slope = slopeElement?.GetDouble() ?? 1;
            var intensity = (rawIntensity * slope) + intercept;

            return (int)intensity;
        }

        /// <summary>
        /// Computes the RGB Value for an intensity value.
        /// </summary>
        /// <param name="pixelIntensity">Intensity value of a pixel</param>
        /// <param name="file">DICOM File containing the pixel</param>
        /// <param name="windowWidth">Option to set own window width</param>
        /// <param name="windowCenter">Option to set own window center</param>
        /// <returns>The resulting Color</returns>
        public static Color32 GetRGBValue(int pixelIntensity, DiFile file, double windowWidth = double.MinValue,
            double windowCenter = double.MinValue)
        {
            var bitsStored = file.GetBitsStored();
            const int rgbRange = 255;

            var windowCenterElement = file.GetElement(0x0028, 0x1050);
            var windowWidthElement = file.GetElement(0x0028, 0x1051);
            var interceptElement = file.GetElement(0x0028, 0x1052);
            var slopeElement = file.GetElement(0x0028, 0x1053);

            var intercept = interceptElement?.GetDouble() ?? 0;
            var slope = slopeElement?.GetDouble() ?? 1;
            double intensity = pixelIntensity;

            if (windowCenter > double.MinValue && windowWidth > double.MinValue)
            {
                intensity = ApplyWindow(intensity, windowWidth, windowCenter);
            }
            else if (windowCenterElement != null && windowWidthElement != null)
            {
                intensity = ApplyWindow(pixelIntensity, windowWidthElement.GetDouble(),
                    windowCenterElement.GetDouble());
            }
            else
            {
                var oldMax = Math.Pow(2, bitsStored) * slope + intercept;
                var oRange = oldMax - intercept;

                intensity = ((intensity - intercept) * rgbRange) / oRange;
            }
            var result = (byte)Math.Round(intensity);

            return new Color32(result, result, result, rgbRange);
        }

        /// <summary>
        /// Applies the windowWidth and windowCenter attributes from a DICOM file.
        /// </summary>
        /// <param name="val">intensity value the window will be applied to</param>
        /// <param name="width">width of the window</param>
        /// <param name="center">center of the window</param>
        /// <returns></returns>
        public static double ApplyWindow(double val, double width, double center)
        {
            var intensity = val;

            if (intensity <= center - 0.5 - (width-1) / 2)
            {
                intensity = 0;
            }
            else if (intensity > center - 0.5 + (width-1) / 2)
            {
                intensity = 255;
            }
            else
            {
                //0 for rgb min value and 255 for rgb max value
                intensity = ((intensity - (center - 0.5)) / (width - 1) + 0.5) * 255;
            }

            return intensity;
        }

        public class TextureUpdate : UnityEvent<SliceType, int>{}

    }
}