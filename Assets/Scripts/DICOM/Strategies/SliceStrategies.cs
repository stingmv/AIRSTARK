using System;
using System.Collections.Generic;
using DICOMParser;
using UnityEngine;

namespace DICOMViews.Strategies
{
    public interface ISliceStrategy
    {
        int GetMaxIndex(int width, int height, int slices);
        int GetTargetWidth(int width, int height, int slices);
        int GetTargetHeight(int width, int height, int slices);
        void FillPixels(int id, int[] data, int width, int height, IReadOnlyList<DiFile> files, Color32[] texData, Func<Color32, Color32> pShader, double windowWidth, double windowCenter);
    }

    public static class SliceStrategyFactory
    {
        public static ISliceStrategy GetStrategy(SliceType type)
        {
            switch (type)
            {
                case SliceType.Transversal: return new TransversalSliceStrategy();
                case SliceType.Frontal: return new FrontalSliceStrategy();
                case SliceType.Sagittal: return new SagittalSliceStrategy();
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }

    public class TransversalSliceStrategy : ISliceStrategy
    {
        public int GetMaxIndex(int width, int height, int slices) => slices;
        public int GetTargetWidth(int width, int height, int slices) => width;
        public int GetTargetHeight(int width, int height, int slices) => height;

        public void FillPixels(int id, int[] data, int width, int height, IReadOnlyList<DiFile> files, Color32[] texData, Func<Color32, Color32> pShader, double windowWidth, double windowCenter)
        {
            var idxPartId = id * width * height;
            var file = files[id];

            for (var y = 0; y < height; ++y)
            {
                var idxPart = idxPartId + y * width;
                for (var x = 0; x < width; ++x)
                {
                    var index = y * width + x;
                    texData[index] = pShader(ImageStack.GetRGBValue(data[idxPart + x], file, windowWidth, windowCenter));
                }
            }
        }
    }

    public class FrontalSliceStrategy : ISliceStrategy
    {
        public int GetMaxIndex(int width, int height, int slices) => height;
        public int GetTargetWidth(int width, int height, int slices) => width;
        public int GetTargetHeight(int width, int height, int slices) => slices;

        public void FillPixels(int id, int[] data, int width, int height, IReadOnlyList<DiFile> files, Color32[] texData, Func<Color32, Color32> pShader, double windowWidth, double windowCenter)
        {
            for (var i = 0; i < files.Count; ++i)
            {
                var idxPart = i * width * height + id * width;
                var file = files[i];

                for (var x = 0; x < width; ++x)
                {
                    var index = i * width + x;
                    texData[index] = pShader(ImageStack.GetRGBValue(data[idxPart + x], file, windowWidth, windowCenter));
                }
            }
        }
    }

    public class SagittalSliceStrategy : ISliceStrategy
    {
        public int GetMaxIndex(int width, int height, int slices) => width;
        public int GetTargetWidth(int width, int height, int slices) => height;
        public int GetTargetHeight(int width, int height, int slices) => slices;

        public void FillPixels(int id, int[] data, int width, int height, IReadOnlyList<DiFile> files, Color32[] texData, Func<Color32, Color32> pShader, double windowWidth, double windowCenter)
        {
            for (var i = 0; i < files.Count; ++i)
            {
                var idxPart = i * width * height + id;
                var file = files[i];

                for (var y = 0; y < height; ++y)
                {
                    var index = i * width + y;
                    texData[index] = pShader(ImageStack.GetRGBValue(data[idxPart + y * width], file, windowWidth, windowCenter));
                }
            }
        }
    }
}
