namespace DICOMViews.Loaders
{
    public static class DicomLoaderFactory
    {
        public static IDicomLoader CreateLoader()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidDicomLoader();
#else
            return new LocalDicomLoader();
#endif
        }
    }
}
