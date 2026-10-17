"""Export an untrained policy and check deterministic ONNX actions against Torch.

This checks the Python model interface only. It is not training or a Unity handshake.
"""
import argparse
from pathlib import Path
import numpy as np
from mlagents.torch_utils import torch
from mlagents_envs.base_env import ActionSpec,BehaviorSpec,DecisionSteps,DimensionProperty,ObservationSpec,ObservationType
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents.trainers.torch_entities.model_serialization import ModelSerializer
from evaluate_traversal import ExportedPolicy


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    observations=[ObservationSpec((n,),(DimensionProperty.NONE,),ObservationType.DEFAULT,name) for n,name in [(28,'vector'),(33,'rays')]]
    spec=BehaviorSpec(observations,ActionSpec(4,np.array([3,3,2,2],dtype=np.int32)))
    policy=TorchPolicy(17,spec,NetworkSettings(normalize=True,hidden_units=128,num_layers=2),SimpleActor,{})
    stem=args.output/'untrained-schema-check';ModelSerializer(policy).export_policy_model(str(stem))
    exported=ExportedPolicy(stem.with_suffix('.onnx'));rng=np.random.RandomState(17)
    for batch in [1,3]:
        obs=[rng.normal(size=(batch,*s.shape)).astype(np.float32) for s in observations]
        masks=[np.zeros((batch,n),dtype=bool) for n in [3,3,2,2]];masks[0][:,2]=True
        steps=DecisionSteps(obs,np.zeros(batch),np.arange(batch),masks,np.zeros(batch,dtype=np.int32),np.zeros(batch))
        result=exported.actions(steps,spec)
        with torch.no_grad():
            expected=policy.actor([torch.as_tensor(o) for o in obs],torch.as_tensor(1-np.concatenate(masks,axis=1).astype(np.float32)))
        np.testing.assert_allclose(result.continuous,expected[4].numpy(),rtol=1e-4,atol=1e-5)
        np.testing.assert_array_equal(result.discrete,expected[7].numpy())
    print('PASS: deterministic hybrid ONNX actions match Torch for batches 1 and 3; no trained policy or Unity result is implied.')


if __name__=='__main__':main()
