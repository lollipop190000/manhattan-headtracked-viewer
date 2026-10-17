using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
namespace Manhattan.TraversalLab
{
    public static class TraversalAgentLauncher
    {
        public static void Install(GameObject target)
        {
            var behavior=target.AddComponent<BehaviorParameters>();behavior.BehaviorName="TraversalFlow";
            behavior.BrainParameters.VectorObservationSize=28;behavior.BrainParameters.NumStackedVectorObservations=1;
            behavior.BrainParameters.ActionSpec=new ActionSpec(4,new[]{3,3,2,2});behavior.UseChildSensors=true;
            var rayRoot=new GameObject("Horizontal building rays");rayRoot.transform.SetParent(target.transform,false);
            var ray=rayRoot.AddComponent<RayPerceptionSensorComponent3D>();ray.SensorName="building-rays";ray.RaysPerDirection=5;
            ray.MaxRayDegrees=180;ray.RayLength=35;ray.SphereCastRadius=.1f;ray.RayLayerMask=1<<30;
            ray.StartVerticalOffset=0;ray.EndVerticalOffset=0;ray.DetectableTags=new List<string>{"Untagged"};
            target.AddComponent<TraversalAgent>();
            var requester=target.AddComponent<DecisionRequester>();requester.DecisionPeriod=5;requester.TakeActionsBetweenDecisions=true;
        }
    }
}
