using System.Collections.Generic;

using UnityEngine;

public class ShipSawRotation : MonoBehaviour
{
    [Header("톱니 회전")]
    [Tooltip("톱니가 로컬 Y축으로 회전하는 초당 각도")]
    [SerializeField] private float _rotationSpeed = 360f;
    private readonly List<Transform> _saws = new List<Transform>();

    void Awake()
    {
        foreach (Transform part in GetComponentsInChildren<Transform>(true))
        {
            if (part.name == "Ice Saw" || part.name == "IceSaw") _saws.Add(part);
        }
    }

    void Update()
    {
        float angle = _rotationSpeed * Time.deltaTime;
        foreach (Transform saw in _saws) saw.Rotate(0f, angle, 0f, Space.Self);
    }
}
