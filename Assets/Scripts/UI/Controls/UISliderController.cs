using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

namespace DICOMViews
{
    [System.Serializable]
    public class UISliderChangedEvent : UnityEvent<UISliderController> { }

    [RequireComponent(typeof(Slider))]
    public class UISliderController : MonoBehaviour
    {
        [Header("UI References")]
        public TMP_Text valueLabel; // opcional, para mostrar el valor

        [Header("Value Settings")]
        [SerializeField] private double _min = 0f;
        [SerializeField] private double _max = 100f;
        [SerializeField] private double _current = 50f;

        [Header("Events")]
        public UISliderChangedEvent SliderChangedEvent = new UISliderChangedEvent();

        private Slider _slider;

        // === Propiedades equivalentes al TubeSlider ===
        public double MinimumValue
        {
            get => _min;
            set
            {
                _min = value;
                UpdateSliderRange();
            }
        }

        public double MaximumValue
        {
            get => _max;
            set
            {
                _max = value;
                UpdateSliderRange();
            }
        }

        public double CurrentDouble
        {
            get => _current;
            set
            {
                _current = Mathf.Clamp((float)value, (float)_min, (float)_max);
                UpdateSliderUI();
            }
        }
        public float CurrentFloat
        {
            get => (float)_current;
            set
            {
                _current = Mathf.Clamp(value, (float)_min, (float)_max);
                UpdateSliderUI();
            }
        }
        //public int CurrentInt => Mathf.RoundToInt((float)_current);
        public int CurrentInt
        {
            get => Mathf.RoundToInt((float)_current);
            set
            {
                _current = Mathf.Clamp(value, (float)_min, (float)_max);
                UpdateSliderUI();
            }
        }

        public double CurrentPercentage
        {
            get
            {
                // Normalizado entre 0 y 1
                return Mathf.InverseLerp((float)_min, (float)_max, (float)_current);
            }
            set
            {
                // Clamp al rango [0,1], luego mapear a [_min,_max]
                float clamped = Mathf.Clamp01((float)value);
                _current = Mathf.Lerp((float)_min, (float)_max, clamped);
                UpdateSliderUI();
                // En el TubeSlider, al setear CurrentPercentage se invocaba el evento.
                SliderChangedEvent.Invoke(this);
            }
        }


        private void Awake()
        {
            _slider = GetComponent<Slider>();
            _slider.wholeNumbers = false;
        }

        private void Start()
        {
            UpdateSliderRange();
            UpdateSliderUI();

            _slider.onValueChanged.AddListener(OnSliderValueChanged);
        }

        private void OnSliderValueChanged(float newValue)
        {
            _current = Mathf.Lerp((float)_min, (float)_max, newValue);
            UpdateLabel();
            SliderChangedEvent.Invoke(this);
        }

        private void UpdateSliderRange()
        {
            _slider.minValue = 0f;
            _slider.maxValue = 1f;
        }

        private void UpdateSliderUI()
        {
            // Convierte el valor actual al rango normalizado del slider
            float normalized = Mathf.InverseLerp((float)_min, (float)_max, (float)_current);
            _slider.SetValueWithoutNotify(normalized);
            UpdateLabel();
        }

        private void UpdateLabel()
        {
            if (valueLabel != null)
                valueLabel.text = $"{_current:0.##}";
        }
    }
}
