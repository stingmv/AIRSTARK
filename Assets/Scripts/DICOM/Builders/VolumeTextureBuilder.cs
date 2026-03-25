using System.Collections.Generic;
using System.Threading.Tasks;
using DICOMParser;
using Threads;
using UnityEngine;

namespace DICOMViews.Builders
{
    public class VolumeTextureBuilder
    {
        private readonly ComputeShader _computeShader;
        private readonly IReadOnlyList<DiFile> _files;
        private readonly int[] _data;
        private readonly int _width;
        private readonly int _height;
        private readonly double _windowWidth;
        private readonly double _windowCenter;

        public VolumeTextureBuilder(ComputeShader computeShader, IReadOnlyList<DiFile> files, int[] data, int width, int height, double windowWidth, double windowCenter)
        {
            _computeShader = computeShader;
            _files = files;
            _data = data;
            _width = width;
            _height = height;
            _windowWidth = windowWidth;
            _windowCenter = windowCenter;
        }

        public async Task<Texture3D> BuildVolumeAsync(ThreadGroupState threadGroupState)
        {
            var volumeTexture = new Texture3D(_width, _height, _files.Count, TextureFormat.ARGB32, false);
            var cols = new Color32[_width * _height * _files.Count];

            if (_computeShader != null)
            {
                await BuildVolumeGPUAsync(threadGroupState, cols);
            }
            else
            {
                // Fallback a CPU (6 hilos por defecto)
                await BuildVolumeCPUAsync(threadGroupState, cols, 6);
            }

            volumeTexture.SetPixels32(cols);
            volumeTexture.Apply();

            return volumeTexture;
        }

        private async Task BuildVolumeGPUAsync(ThreadGroupState groupState, Color32[] target)
        {
            groupState?.Register();
            int totalPixels = target.Length;
            
            ComputeBuffer dataBuffer = new ComputeBuffer(_data.Length, sizeof(int));
            dataBuffer.SetData(_data);
            
            ComputeBuffer colorBuffer = new ComputeBuffer(totalPixels, sizeof(uint));

            int kernelIdx = _computeShader.FindKernel("ProcessVolume");
            _computeShader.SetBuffer(kernelIdx, "DataBuffer", dataBuffer);
            _computeShader.SetBuffer(kernelIdx, "ResultColors", colorBuffer);
            _computeShader.SetInt("Width", _width);
            _computeShader.SetInt("Height", _height);
            _computeShader.SetInt("Depth", _files.Count);
            
            float effectiveWindowWidth = (float)_windowWidth;
            float effectiveWindowCenter = (float)_windowCenter;

            if (_windowCenter <= double.MinValue || _windowWidth <= double.MinValue)
            {
                var file = _files[0];
                var centerEl = file.GetElement(0x0028, 0x1050);
                var widthEl = file.GetElement(0x0028, 0x1051);

                if (centerEl != null && widthEl != null)
                {
                    effectiveWindowCenter = (float)centerEl.GetDouble();
                    effectiveWindowWidth = (float)widthEl.GetDouble();
                }
                else
                {
                    int bitsStored = file.GetBitsStored();
                    var interceptEl = file.GetElement(0x0028, 0x1052);
                    var slopeEl = file.GetElement(0x0028, 0x1053);
                    double intercept = interceptEl?.GetDouble() ?? 0;
                    double slope = slopeEl?.GetDouble() ?? 1;

                    double oldMax = System.Math.Pow(2, bitsStored) * slope + intercept;
                    double oRange = oldMax - intercept;

                    effectiveWindowWidth = (float)oRange;
                    effectiveWindowCenter = (float)(intercept + oRange / 2.0);
                }
            }

            _computeShader.SetFloat("WindowWidth", effectiveWindowWidth);
            _computeShader.SetFloat("WindowCenter", effectiveWindowCenter);

            int tX = Mathf.CeilToInt(_width / 8f);
            int tY = Mathf.CeilToInt(_height / 8f);
            int tZ = Mathf.CeilToInt(_files.Count / 8f);

            _computeShader.Dispatch(kernelIdx, tX, tY, tZ);

            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(colorBuffer);
            while (!request.done)
            {
                await Task.Yield();
            }

            if (!request.hasError)
            {
                var nativeArray = request.GetData<Color32>();
                nativeArray.CopyTo(target);
            }
            else
            {
                Debug.LogError("Error recuperando memoria de GPU en DicomComputeShader");
            }

            dataBuffer.Release();
            colorBuffer.Release();

            groupState.TotalProgress = 1;
            groupState?.IncrementProgress();
            groupState?.Done();
        }

        private async Task BuildVolumeCPUAsync(ThreadGroupState groupState, Color32[] target, int threadCount)
        {
            var spacing = _files.Count / threadCount;
            var tasks = new System.Collections.Generic.List<Task>();

            for (var i = 0; i < threadCount; ++i)
            {
                groupState.Register();
                var startIndex = i * spacing;
                var endIndex = (i + 1 == threadCount) ? _files.Count : startIndex + spacing;

                tasks.Add(Task.Run(() => ComputeSlicesCPU(groupState, target, startIndex, endIndex)));
            }
            await Task.WhenAll(tasks);
        }

        private void ComputeSlicesCPU(ThreadGroupState groupState, Color32[] target, int start, int end)
        {
            var idx = start * _width * _height;

            for (var z = start; z < end; ++z)
            {
                var idxPartZ = z * _width * _height;
                for (var y = 0; y < _height; ++y)
                {
                    var idxPart = idxPartZ + y * _width;
                    for (var x = 0; x < _width; ++x, ++idx)
                    {
                        var rgb = ImageStack.GetRGBValue(_data[idxPart + x], _files[z], _windowWidth, _windowCenter);
                        target[idx] = DICOMParser.TransferFunction.DYN_ALPHA(rgb);
                    }
                }

                System.Threading.Thread.Sleep(5);
                groupState.IncrementProgress();
            }

            groupState.Done();
        }
    }
}
