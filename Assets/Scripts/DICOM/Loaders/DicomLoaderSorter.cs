using System.Collections.Generic;
using DICOMParser;

namespace DICOMViews.Loaders
{
    public static class DicomLoaderSorter
    {
        public static DiFile[] Sort(List<DiFile> parsedFiles)
        {
            if (parsedFiles.Count == 0) return new DiFile[0];
            
            var result = new DiFile[parsedFiles.Count];
            bool zeroBased = true;
            
            foreach (var diFile in parsedFiles)
            {
                if (zeroBased && diFile.GetImageNumber() == result.Length)
                {
                    ShiftLeft(result);
                    zeroBased = false;
                }

                int index = zeroBased ? diFile.GetImageNumber() : diFile.GetImageNumber() - 1;
                
                if (index >= 0 && index < result.Length)
                {
                    result[index] = diFile;
                }
            }
            
            return result;
        }

        private static void ShiftLeft<T>(IList<T> array)
        {
            for (var index = 0; index < array.Count - 1; index++)
            {
                array[index] = array[index + 1];
            }
            array[array.Count - 1] = default(T);
        }
    }
}
