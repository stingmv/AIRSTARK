using DICOMViews.Events;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DICOMViews
{
    [RequireComponent(typeof(RectTransform))]
    public class PixelClickHandler : MonoBehaviour, IPointerClickHandler
    {
        private Camera _mainCamera;
        private RectTransform _rectTransform;

        public PixelClicked OnPixelClick = new PixelClicked();

#if UNITY_EDITOR
        private bool _clicked = false;
#endif

        private void Start()
        {
            // Encuentra la cámara principal (por defecto la del XR Rig o Camera Offset)
            _mainCamera = Camera.main;
            _rectTransform = GetComponent<RectTransform>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_mainCamera == null)
                _mainCamera = Camera.main;

            Vector2 localPos;

            // Convierte el punto de clic en coordenadas locales del rectángulo
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform,
                eventData.pressPosition,
                _mainCamera,
                out localPos);

#if UNITY_EDITOR
            if (_clicked)
            {
                _clicked = false;
                return;
            }
            _clicked = true;
#endif

            OnPixelSelected(localPos);
        }

        /// <summary>
        /// Convierte la posición local del clic a coordenadas normalizadas (0-1)
        /// y emite el evento.
        /// </summary>
        private void OnPixelSelected(Vector2 textureSpace)
        {
            var rect = _rectTransform.rect;
            float xCur = (textureSpace.x - rect.xMin) / rect.width;
            float yCur = (textureSpace.y - rect.yMin) / rect.height;

            // Clamp para evitar valores fuera de rango por clics en los bordes
            xCur = Mathf.Clamp01(xCur);
            yCur = Mathf.Clamp01(yCur);

            OnPixelClick.Invoke(xCur, yCur);
        }
    }
}
