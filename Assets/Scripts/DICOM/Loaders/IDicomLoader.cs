using System.Threading.Tasks;
using DICOMParser;
using Threads;

namespace DICOMViews.Loaders
{
    public interface IDicomLoader
    {
        Task<DiFile[]> LoadFilesAsync(string folderPath, ThreadGroupState groupState);
    }
}
