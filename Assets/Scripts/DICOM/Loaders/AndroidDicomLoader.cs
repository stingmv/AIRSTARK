using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DICOMParser;
using Threads;
using UnityEngine;
using UnityEngine.Networking;

namespace DICOMViews.Loaders
{
    public class AndroidDicomLoader : IDicomLoader
    {
        public async Task<DiFile[]> LoadFilesAsync(string folderPath, ThreadGroupState groupState)
        {
            var tempList = new List<DiFile>();
            string indexPath = Path.Combine(folderPath, "index.txt");
            
            var www = UnityWebRequest.Get(indexPath);
            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("ERROR: No se pudo cargar index.txt en Android: " + indexPath);
                return new DiFile[0];
            }
            
            string content = www.downloadHandler.text;
            string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            groupState.TotalProgress = lines.Length;

            foreach (var line in lines)
            {
                string clean = line.Trim();
                if (string.IsNullOrWhiteSpace(clean)) continue;
                
                string fullPath = Path.Combine(folderPath, clean);
                
                var fileWww = UnityWebRequest.Get(fullPath);
                var fileOp = fileWww.SendWebRequest();
                while (!fileOp.isDone) await Task.Yield();
                
                if (fileWww.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Error cargando archivo DICOM: " + fullPath + " -> " + fileWww.error);
                    groupState.TotalProgress--;
                    continue;
                }
                
                byte[] dicomBytes = fileWww.downloadHandler.data;
                var diFile = new DiFile();
                diFile.InitFromBytes(dicomBytes);
                tempList.Add(diFile);
                
                groupState.IncrementProgress();
            }
            
            return DicomLoaderSorter.Sort(tempList);
        }
    }
}
