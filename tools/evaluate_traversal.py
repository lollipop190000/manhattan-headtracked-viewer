"""Matched-map PPO evaluation. Requires a built Unity lab and real exported models."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import statistics


def read_rows(directory):
    rows=[]
    for path in sorted(Path(directory).glob('episodes-*.csv')):
        with path.open(encoding='utf-8-sig',newline='') as stream:rows.extend(csv.DictReader(stream))
    if rows and all(r.get('completed_utc') for r in rows):rows.sort(key=lambda r:r['completed_utc'])
    return rows


def summarize(rows):
    if not rows:raise ValueError('No completed Unity episodes; no measurements can be reported.')
    duration=sum(float(r['duration']) for r in rows)
    if duration<=0:raise ValueError('Episode duration must be positive.')
    successes=[r for r in rows if int(r['success'])==1]
    transitions=sum(int(r['transitions']) for r in rows)
    return dict(episodes=len(rows),success_rate=len(successes)/len(rows),simulated_seconds=duration,
        snags=sum(int(r['snags']) for r in rows),stucks=sum(int(r['stucks']) for r in rows),
        snags_per_minute=60*sum(int(r['snags']) for r in rows)/duration,
        stucks_per_minute=60*sum(int(r['stucks']) for r in rows)/duration,
        mean_speed=sum(float(r['mean_speed'])*float(r['duration']) for r in rows)/duration,
        air_fraction=sum(float(r['air_fraction'])*float(r['duration']) for r in rows)/duration,
        mean_transition_loss=sum(float(r['transition_loss_total']) for r in rows)/transitions if transitions else 0,
        transitions=transitions,goal_seconds=statistics.mean(float(r['goal_time']) for r in successes) if successes else None,
        swing_fraction=sum(float(r['swing_fraction'])*float(r['duration']) for r in rows)/duration)


def validate_schedule(rows,base_seed,maps,episodes):
    expected={(base_seed+map_index,map_index*episodes+e) for map_index in range(maps) for e in range(episodes)}
    actual=[(int(r['seed']),int(r['episode'])) for r in rows]
    if len(actual)!=len(expected) or set(actual)!=expected:raise ValueError('Unity logs do not match the requested fixed-seed schedule.')


class ExportedPolicy:
    def __init__(self,path):
        import onnx
        from onnx.reference import ReferenceEvaluator
        model_bytes=Path(path).read_bytes();self.sha256=hashlib.sha256(model_bytes).hexdigest()
        model=onnx.load_model_from_string(model_bytes)
        names={x.name for x in model.graph.output}
        self.outputs=['deterministic_continuous_actions','deterministic_discrete_actions']
        if not set(self.outputs)<=names:raise ValueError('Export must contain deterministic continuous and discrete action outputs.')
        # Keep only ancestors of deterministic outputs. Stochastic Multinomial
        # export branches are unrelated to evaluation and unavailable in ONNX 1.15's reference engine.
        model=onnx.utils.Extractor(model).extract_model([x.name for x in model.graph.input],self.outputs)
        self.session=ReferenceEvaluator(model);self.inputs=model.graph.input

    def actions(self,steps,spec):
        import numpy as np
        from mlagents_envs.base_env import ActionTuple
        feed={}
        for node in self.inputs:
            if node.name.startswith('obs_'):feed[node.name]=steps.obs[int(node.name[4:])].astype(np.float32)
            elif node.name=='action_masks':
                feed[node.name]=1-np.concatenate(steps.action_mask,axis=1).astype(np.float32) if steps.action_mask is not None else np.ones((len(steps),sum(spec.action_spec.discrete_branches)),dtype=np.float32)
            elif node.name=='recurrent_in':
                shape=[len(steps)]+[x.dim_value if x.HasField('dim_value') else 1 for x in node.type.tensor_type.shape.dim[1:]]
                if shape[-1]!=0:raise ValueError('This evaluator expects the non-recurrent supplied PPO configuration.')
                feed[node.name]=np.zeros(shape,dtype=np.float32)
            else:raise ValueError('Unknown model input '+node.name)
        continuous,discrete=self.session.run(self.outputs,feed)
        return ActionTuple(continuous=continuous.astype(np.float32),discrete=discrete.astype(np.int32))


def evaluate_one(args,label,model,condition,index):
    from mlagents_envs.environment import UnityEnvironment
    from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
    from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel
    folder=args.output/label;folder.mkdir()
    channel=EngineConfigurationChannel();channel.set_configuration_parameters(time_scale=args.time_scale,quality_level=0,target_frame_rate=-1)
    stats=StatsSideChannel()
    policy=ExportedPolicy(model)
    env=UnityEnvironment(file_name=str(args.env),base_port=args.base_port,worker_id=index,seed=args.seed,no_graphics=True,
        side_channels=[channel,stats],log_folder=str(folder),timeout_wait=120,additional_args=['--traversal-agent','--evaluation','--flow',condition,
        '--map-seed',str(args.seed),'--episodes-per-map',str(args.episodes),'--telemetry-dir',str(folder)])
    try:
        env.reset();behavior=next(iter(env.behavior_specs));spec=env.behavior_specs[behavior]
        if spec.action_spec.continuous_size!=4 or list(spec.action_spec.discrete_branches)!=[3,3,2,2]:raise ValueError('Policy and controller action specifications differ.')
        completed=0;decisions=0;limit=args.maps*args.episodes*2000
        while completed<args.maps*args.episodes:
            steps,terminal=env.get_steps(behavior);completed+=len(terminal)
            if completed>=args.maps*args.episodes:break
            if len(steps):env.set_actions(behavior,policy.actions(steps,spec))
            env.step();stats.get_and_reset_stats();decisions+=1
            if decisions>limit:raise RuntimeError('Evaluation did not complete; no partial result is presented as complete.')
    finally:env.close()
    rows=read_rows(folder);validate_schedule(rows,args.seed,args.maps,args.episodes)
    summary=summarize(rows);summary.update(label=label,condition=condition,policy_sha256=policy.sha256)
    with (folder/'events.csv').open('w',encoding='utf-8',newline='') as target:
        writer=None
        for path in sorted(folder.glob('snags-*.csv')):
            with path.open(encoding='utf-8-sig',newline='') as source:
                reader=csv.DictReader(source)
                if writer is None:writer=csv.DictWriter(target,fieldnames=reader.fieldnames);writer.writeheader()
                writer.writerows(reader)
    return summary


def main():
    p=argparse.ArgumentParser(description=__doc__)
    for name in ['env','common','off','on','output']:p.add_argument('--'+name,type=Path,required=True)
    p.add_argument('--maps',type=int,default=16);p.add_argument('--episodes',type=int,default=8)
    p.add_argument('--seed',type=int,default=1000);p.add_argument('--base-port',type=int,default=5505);p.add_argument('--time-scale',type=float,default=20)
    args=p.parse_args()
    for path in [args.env,args.common,args.off,args.on]:
        if not path.is_file():p.error('Required real build/model is missing: '+str(path))
    if args.maps<1 or args.episodes<1:p.error('Map and episode counts must be positive.')
    args.output.mkdir(parents=True,exist_ok=False)
    runs=[('common-off',args.common,'off'),('common-on',args.common,'on'),('retrained-off',args.off,'off'),('retrained-on',args.on,'on')]
    result=[]
    for index,(label,model,condition) in enumerate(runs):result.append(evaluate_one(args,label,model,condition,index))
    if result[0]['policy_sha256']!=result[1]['policy_sha256']:raise AssertionError('The same-policy comparison changed policies.')
    with (args.output/'summary.csv').open('w',encoding='utf-8',newline='') as stream:
        writer=csv.DictWriter(stream,fieldnames=list(result[0]));writer.writeheader();writer.writerows(result)
    (args.output/'summary.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    lines=['| Condition | Success | Snag/min | Stuck/min | Speed m/s | Air % | Transition loss m/s | Goal s |','|---|---:|---:|---:|---:|---:|---:|---:|']
    for r in result:lines.append('| {} | {:.1%} | {:.3f} | {:.3f} | {:.3f} | {:.1%} | {:.3f} | {} |'.format(r['label'],r['success_rate'],r['snags_per_minute'],r['stucks_per_minute'],r['mean_speed'],r['air_fraction'],r['mean_transition_loss'],'n/a' if r['goal_seconds'] is None else f"{r['goal_seconds']:.3f}"))
    (args.output/'summary.md').write_text('\n'.join(lines)+'\n',encoding='utf-8');print('\n'.join(lines))


if __name__=='__main__':main()
