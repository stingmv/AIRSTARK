using DICOMParser;
using Segmentation;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Threads;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Events;

namespace DICOMViews
{
    /// <summary>
    /// Manages dependencies between all views and relays events to interested views.
    /// </summary>
    /// En resumen, es el "cerebro" que conecta la carga de archivos, el renderizado 2D/3D y la interfaz.
    public class ViewManager : MonoBehaviour
    {
        // Referencias a componentes de la UI
        public MainMenu MainMenu;
        public WindowSettingsPanel WindowSettingsPanel;
        public Slice2DView Slice2DView;

        // Referencias a los objetos de renderizado 3D
        public GameObject VolumeRenderingParent;
        public GameObject RotationObjectParent;
        public VolumeRendering.VolumeRendering VolumeRendering;

        // Configuración de segmentación
        public SegmentConfiguration SegmentConfiguration;

        // Renderizado volumétrico y raymarching
        public GameObject Volume;
        public RayMarching RayMarching;

        // Barra de herramientas o AppBar
        public GameObject AppBar;

        // Internos: stack de imágenes, caché de segmentación y control de trabajo
        private ImageStack _stack;
        private SegmentCache _segmentCache;
        private ThreadGroupState _currentWorkload;

        // Use this for initialization

        private void Start()
        {
            Debug.Log("EXITO AL EMPEZAR");
            MainMenu.ClearDropdown();

            // Inicia la carga de carpetas desde StreamingAssets usando índice
            StartCoroutine(LoadFoldersFromIndex());
        }

        private IEnumerator LoadFoldersFromIndex()
        {
            List<string> folderNames = new List<string>();//crea lista vacia donde se almacenaran os nombre de carpetas leidos desde el indice

            string indexPath = System.IO.Path.Combine(Application.streamingAssetsPath, "index.txt");//construye la ruta completa hacia index.txt dentro de StreamingAssets
            string indexContent;//variable para guardar contenido de index una vez leido

            if (indexPath.Contains("://") || indexPath.Contains(":///")) // Comprueba si las rutas contienen :// o :/// que corresponde a Android/Quest
            {
                Debug.Log("ES ANDROID");
                UnityWebRequest www = UnityWebRequest.Get(indexPath);//Crea un get para la ruta indexpath
                yield return www.SendWebRequest();//envia peticion y espera su completitud

                if (www.result != UnityWebRequest.Result.Success)//revisa el resultado de la request
                {
                    Debug.LogError("Error cargando índice de carpetas: " + www.error);
                    yield break;
                }

                indexContent = www.downloadHandler.text;//extrae contenido textual del archivo desde downloadhandler
                Debug.Log("INDEX CONTENT ANDROID: " + indexContent);

            }
            else
            {
                Debug.Log("ES PC");
                indexContent = System.IO.File.ReadAllText(indexPath); // Editor / PC
                                                                      //como en este caso seria pc, lee el archivo directamente sin unitywebrequest
                Debug.Log("INDEX CONTENT PC: " + indexContent);

            }

            // Cada línea es un nombre de carpeta
            string[] lines = indexContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            folderNames.AddRange(lines);//añade todas las lineas leidas a foldernames

            // Agrega al menú principal
            MainMenu.AddDropdownOptions(folderNames);//pasa al UI principal la lista de nombres

            // Inicialización normal de ImageStack y SegmentCache
            _stack = gameObject.AddComponent<ImageStack>();
            _stack.OnTextureUpdate.AddListener(Slice2DView.TextureUpdated);

            _segmentCache = gameObject.AddComponent<SegmentCache>();
            _segmentCache.TextureReady.AddListener(Slice2DView.SegmentTextureUpdated);
            _segmentCache.SegmentChanged.AddListener(SegmentChanged);

            Slice2DView.SegmentCache = _segmentCache;

            Debug.Log("AVANZANDO");

            // Configura listeners de la UI
            WindowSettingsPanel.OnSettingsChangedEvent.AddListener(_stack.OnWindowSettingsChanged);
            WindowSettingsPanel.gameObject.SetActive(false);

            VolumeRenderingParent.SetActive(false);
            RotationObjectParent.SetActive(true);
            Slice2DView.gameObject.SetActive(false);
            SegmentConfiguration.transform.gameObject.SetActive(false);

            SegmentConfiguration.OnSelectionChanged2D.AddListener(SelectionChanged2D);
            SegmentConfiguration.OnSelectionChanged3D.AddListener(SelectionChanged3D);
            SegmentConfiguration.OnHideBaseChanged.AddListener(HideBaseChanged);

            Slice2DView.OnPointSelected.AddListener(SegmentConfiguration.UpdateRegionSeed);

            MainMenu.DisableButtons();

            Debug.Log("AVANZANDO 2");
        }

        // Update is called once per frame
        private void Update ()
        {
            if (_currentWorkload != null && _currentWorkload.TotalProgress > 0)
            {
                MainMenu.ProgressHandler.Max = _currentWorkload.TotalProgress;
                MainMenu.ProgressHandler.Value = _currentWorkload.Progress;
            }
        }

        /// <summary>
        /// Inicia el parseo de archivos DICOM de la carpeta seleccionada.
        /// </summary>
        public async void ParseFiles()
        {
            // Si el usuario no seleccionó carpeta válida, salir.
            if (MainMenu.GetSelectedFolder() == MainMenu.FolderHint)
            {
                return;
            }

            // Bloquea UI para evitar inputs mientras se cargan archivos.
            MainMenu.DisableDropDown();
            WindowSettingsPanel.DisableButtons();
            MainMenu.DisableButtons();

            WindowSettingsPanel.gameObject.SetActive(false);
            SegmentConfiguration.transform.gameObject.SetActive(false);
            Debug.Log("PARSE FILES LLAMADO");

            _currentWorkload = new ThreadGroupState();
            MainMenu.ProgressHandler.TaskDescription = "Cargando Archivos";
            MainMenu.ProgressHandler.Value = 0;

            // Añade el trabajo asíncrono.
            await _stack.StartParsingFiles(Path.Combine(Application.streamingAssetsPath, MainMenu.GetSelectedFolder()), _currentWorkload);
            
            Debug.Log("PATH COMBINE:" + Path.Combine(Application.streamingAssetsPath, MainMenu.GetSelectedFolder()));

            _currentWorkload = null;
            MainMenu.ProgressHandler.Max = 0;
            MainMenu.ProgressHandler.Value = 0;
            OnFilesParsed();
        }

        /// <summary>
        /// Parsing of files has been completed.
        /// Callback cuando el parsing de archivos ha finalizado.
        /// </summary>
        private void OnFilesParsed()
        {
            // Configura el panel de ventanas con valores min/max y presets obtenidos por ImageStack.
            WindowSettingsPanel.Configure(
                _stack.MinPixelIntensity, 
                _stack.MaxPixelIntensity,
                _stack.WindowWidthPresets,
                _stack.WindowCenterPresets
            );
            WindowSettingsPanel.gameObject.SetActive(true);

            try
            {
                _segmentCache.InitializeSize(_stack.Width, _stack.Height, _stack.Slices);

                SegmentConfiguration.Initialize(_segmentCache, _stack.MinPixelIntensity, _stack.MaxPixelIntensity);

                Slice2DView.Initialize(_stack);

                //*_workIndicator.FinishedWork();
            }
            catch (Exception e)
            {
                GameObject.FindGameObjectWithTag("DebugText").GetComponent<Text>().text = e.ToString();
            }

            _segmentCache.InitializeTextures();
            SegmentConfiguration.transform.gameObject.SetActive(true);

            PreProcessData();
        }

        /// <summary>
        /// Starts preprocessing the stored DiFiles.
        /// </summary>
        public async void PreProcessData()
        {
            _currentWorkload = new ThreadGroupState();
            MainMenu.ProgressHandler.TaskDescription = "Preprocesamiento de Datos";
            MainMenu.ProgressHandler.Value = 0;

            await _stack.StartPreprocessData(_currentWorkload);

            _currentWorkload = null;
            MainMenu.ProgressHandler.Max = 0;
            MainMenu.ProgressHandler.Value = 0;
            OnPreProcessDone();
        }

        /// <summary>
        /// Preprocessing of the DiFiles is completed.
        /// </summary>
        private void OnPreProcessDone()
        {
            WindowSettingsPanel.EnableButtons();

            MainMenu.EnableButtons();
            MainMenu.EnableDropDown();
            var preview = MainMenu.PreviewImage.texture as Texture2D;

            if (!preview || preview.width != _stack.Width || preview.height != _stack.Height)
            {
                // Could be avoided if single preloaded texture would be stored in imagestack to save memory
                preview = new Texture2D(_stack.Width, _stack.Height, TextureFormat.ARGB32, false);
                MainMenu.PreviewImage.texture = preview;
            }

            var pixels = preview.GetPixels32();

            ImageStack.FillPixelsTransversal(_stack.Slices/2, _stack.GetData(), _stack.Width, _stack.Height, _stack.DicomFiles, pixels);

            preview.SetPixels32(pixels);
            preview.Apply();

            MainMenu.PreviewImage.texture = preview;

            //*_workIndicator.FinishedWork();
        }

        /// <summary>
        /// Starts creating a volume from the current data.
        /// </summary>
        public async void CreateVolume()
        {
            MainMenu.DisableButtons();
            WindowSettingsPanel.DisableButtons();

            _currentWorkload = new ThreadGroupState();
            MainMenu.ProgressHandler.TaskDescription = "Creando Volumen";
            MainMenu.ProgressHandler.Value = 0;

            await _stack.StartCreatingVolume(_currentWorkload);

            _currentWorkload = null;
            MainMenu.ProgressHandler.Max = 0;
            MainMenu.ProgressHandler.Value = 0;
            OnVolumeCreated();
        }

        /// <summary>
        /// Volume creation is completed.
        /// </summary>
        private void OnVolumeCreated()
        {
            VolumeRendering.SetVolume(_stack.VolumeTexture);
            StartCoroutine(_segmentCache.ApplySegments(_stack.VolumeTexture, SegmentConfiguration.Display3Ds, SegmentConfiguration.HideBase));
            VolumeRenderingParent.SetActive(true);
            //RotationObjectParent.SetActive(true);

            //RayMarching.initVolume(_stack.VolumeTexture);
            //Volume.SetActive(true);

            MainMenu.EnableButtons();
            WindowSettingsPanel.EnableButtons();

            //*_workIndicator.FinishedWork();
        }

        /// <summary>
        /// Starts creating 2D Textures for the current data.
        /// </summary>
        public async void CreateTextures()
        {
            MainMenu.DisableButtons();
            WindowSettingsPanel.DisableButtons();
            Slice2DView.gameObject.SetActive(true);

            _currentWorkload = new ThreadGroupState();
            MainMenu.ProgressHandler.TaskDescription = "Creando Texturas";
            MainMenu.ProgressHandler.Value = 0;

            await _stack.StartCreatingTextures(_currentWorkload);

            _currentWorkload = null;
            MainMenu.ProgressHandler.Max = 0;
            MainMenu.ProgressHandler.Value = 0;
            OnTexturesCreated();
        }

        /// <summary>
        /// Texture creation has been completed.
        /// </summary>
        private void OnTexturesCreated()
        {
            //*_workIndicator.FinishedWork();

            MainMenu.EnableButtons();
            WindowSettingsPanel.EnableButtons();

            StartCoroutine(_segmentCache.ApplyTextures(SegmentConfiguration.Display2Ds, true));
        }

        /// <summary>
        /// A segment has been modified.
        /// </summary>
        /// <param name="selector">The selector for the modified segment</param>
        private void SegmentChanged(uint selector)
        {
            StartCoroutine(_segmentCache.ApplyTextures(SegmentConfiguration.Display2Ds, true));

            if (_stack.VolumeTexture)
            {
                CreateVolume();
            }
        }

        /// <summary>
        /// Value changed for visibility of base data
        /// </summary>
        /// <param name="hideBase">new state of visibility</param>
        private void HideBaseChanged(bool hideBase)
        {
#if PRINT_USAGE
            Debug.Log(Time.time +
                      $" : Set hide base data to {hideBase}.");
#endif

            if (_stack.VolumeTexture)
            {
                CreateVolume();
            }
        }

#if PRINT_USAGE
        private void OnApplicationQuit()
        {
            Debug.Log(Time.time +" : Application closed.");    
        }
#endif

        /// <summary>
        /// Visibility of a segment in 2D has changed
        /// </summary>
        /// <param name="selector">new selection of segments to display</param>
        private void SelectionChanged2D(uint selector)
        {
            StartCoroutine(_segmentCache.ApplyTextures(selector, true));
        }

        /// <summary>
        /// Visibility of a segment in 3D has changed
        /// </summary>
        /// <param name="selector">new selection of segments to display</param>
        private void SelectionChanged3D(uint selector)
        {
            CreateVolume();
        }



    }
}
