using System;
using UnityEngine;

namespace Events
{
    public static class DicomEventBus
    {
        // Se llama cuando el usuario selecciona una carpeta de DICOMs
        public static Action<string> OnFolderSelected;

        // Se llama para reportar el progreso global de la carga asíncrona (Current, Max, Descripcion)
        public static Action<int, int, string> OnProgressUpdated;

        // Se llama para reportar que una tarea pesada ha finalizado
        public static Action OnTaskCompleted;
        
        // Se llama cuando se produce un error grave (ej en la recarga)
        public static Action<string> OnError;
    }
}
