using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DICOMParser;
using Threads;
using UnityEngine;

namespace DICOMViews.Loaders
{
    public class LocalDicomLoader : IDicomLoader
    {
        public async Task<DiFile[]> LoadFilesAsync(string folderPath, ThreadGroupState groupState)
        {
            var fileNames = new List<string>();
            var files = Directory.GetFiles(folderPath);

            foreach (var file in files)
            {
                if (File.Exists(file) && (file.EndsWith(".dcm") || !file.Contains(".")))
                {
                    fileNames.Add(file);
                }
            }

            groupState.TotalProgress = fileNames.Count;
            var tempList = new List<DiFile>();

            foreach (var path in fileNames)
            {
                var diFile = new DiFile();
                diFile.InitFromFile(path);
                tempList.Add(diFile);
                
                groupState.IncrementProgress();
                await Task.Yield();
            }

            return DicomLoaderSorter.Sort(tempList);
        }
    }
}
