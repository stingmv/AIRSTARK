using UnityEngine;

/// <summary>
/// MANTIENE UN OBJETO SIEMPRE ORIENTADO A LA CAMARA(por ejemplo, etiquetas o íconos 3D).
/// </summary>
public class Billboard : MonoBehaviour
{
    public enum PivotAxis
    {
        XY,
        Y,
        X,
        Z,
        XZ,
        YZ,
        Free
    }

    [Header("Configuración del eje de rotación")]
    [Tooltip("Define sobre qué ejes el objeto puede rotar para mirar a la cámara.")]
    [SerializeField] private PivotAxis pivotAxis = PivotAxis.XY;

    [Header("Objetivo al que mirar")]
    [Tooltip("Si se deja vacío, se usará la cámara principal (Camera.main).")]
    [SerializeField] private Transform targetTransform;

    private void OnEnable()
    {
        if (targetTransform == null && Camera.main != null)
            targetTransform = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (targetTransform == null)
        {
            if (Camera.main == null) return;
            targetTransform = Camera.main.transform;
        }

        // Dirección desde el objeto hacia la cámara
        Vector3 directionToTarget = targetTransform.position - transform.position;

        bool useCameraAsUpVector = true;

        // Limitar ejes de rotación según la configuración
        switch (pivotAxis)
        {
            case PivotAxis.X:
                directionToTarget.x = 0.0f;
                useCameraAsUpVector = false;
                break;

            case PivotAxis.Y:
                directionToTarget.y = 0.0f;
                useCameraAsUpVector = false;
                break;

            case PivotAxis.Z:
                directionToTarget.x = 0.0f;
                directionToTarget.y = 0.0f;
                break;

            case PivotAxis.XY:
                useCameraAsUpVector = false;
                break;

            case PivotAxis.XZ:
                directionToTarget.x = 0.0f;
                break;

            case PivotAxis.YZ:
                directionToTarget.y = 0.0f;
                break;

            case PivotAxis.Free:
            default:
                break;
        }

        // Evitar errores si está demasiado cerca de la cámara
        if (directionToTarget.sqrMagnitude < 0.001f)
            return;

        // Aplicar rotación
        if (useCameraAsUpVector)
            transform.rotation = Quaternion.LookRotation(-directionToTarget, Camera.main.transform.up);
        else
            transform.rotation = Quaternion.LookRotation(-directionToTarget);
    }
}
