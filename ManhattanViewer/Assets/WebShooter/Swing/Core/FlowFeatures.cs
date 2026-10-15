using System;
using UnityEngine;

namespace WebShooter.Swing
{
    [Serializable]
    public sealed class FlowFeatures
    {
        [SerializeField] public bool preserveSteeringMomentum;
        [Min(0)] public float steeringTurnDegrees = 65f;
        [Min(0)] public float airTargetSpeed = 10f;
    }
}
