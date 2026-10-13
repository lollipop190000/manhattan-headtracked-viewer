import pathlib,json,csv,math,collections
root=pathlib.Path.cwd();out=root/'ownership-visible-comparison-v1-20261010';out.mkdir(exist_ok=True)
def stats(v):
 if not v:return None
 v=sorted(v);q=lambda f:v[max(0,math.ceil(len(v)*f)-1)]
 return dict(n=len(v),p50=q(.5),p95=q(.95),p99=q(.99),maximum=v[-1],framesGE50=sum(x>=50 for x in v))
def run(path):
 report=json.loads((path/'benchmark.json').read_text(encoding='utf-8-sig'))
 frames=list(csv.DictReader((path/'frames.csv').open(encoding='utf-8-sig')))
 timing=list(csv.DictReader((path/'frame-timing.csv').open(encoding='utf-8-sig')))
 result=dict(path=str(path),benchmark=report,runtime=json.loads((path/'runtime-condition.json').read_text(encoding='utf-8-sig')),phases={})
 prior=None
 for r in timing:
  r['wall_interval_ms']=None if prior is None else (float(r['realtime_seconds'])-prior)*1000
  prior=float(r['realtime_seconds'])
 for phase,active in [('startup-warmup',False),('warmed-measurement',True)]:
  rows=[r for r in timing if (r['measurement_active']=='1')==active]
  valid=[];seen=set()
  for r in rows:
   stamp=r['frame_start_timestamp']
   if int(r['timing_sample_count'])>0 and int(stamp)>0 and stamp not in seen:
    seen.add(stamp);valid.append(r)
  vals=lambda key:[float(r[key]) for r in rows if r.get(key) is not None]
  timed=lambda key:[float(r[key]) for r in valid if float(r[key])>0]
  stream=vals('streaming_ms');work=vals('ownership_tick_ms')
  p=dict(rows=len(rows),wallIntervals=stats(vals('wall_interval_ms')),cpuFrame=stats(timed('cpu_frame_ms')),gpuFrame=stats(timed('gpu_frame_ms')),cpuMain=stats(timed('cpu_main_ms')),cpuRender=stats(timed('cpu_render_ms')),presentWait=stats(timed('present_wait_ms')),timingUniqueAvailable=len(valid),gpuPositiveSamples=sum(float(r['gpu_frame_ms'])>0 for r in valid),streamingAll=stats(stream),streamingActiveDiagnostic=stats([v for v in stream if v>=.5]),ownershipAll=stats(work) if work else None,ownershipWorkDiagnostic=stats([v for v in work if v>=.05]),loadedRange=[min(int(r['loaded']) for r in rows),max(int(r['loaded']) for r in rows)],pendingMaximum=max(int(r['pending']) for r in rows),failuresMaximum=max(int(r['failures']) for r in rows),drawCallsRange=[min(int(r['draw_calls']) for r in rows),max(int(r['draw_calls']) for r in rows)],wholeManagedHeapPeak=max(int(r['managed_bytes']) for r in rows),topWallIntervals=sorted(rows,key=lambda r:r['wall_interval_ms'] or 0,reverse=True)[:5])
  result['phases'][phase]=p
 framevals=[float(r['frame_ms']) for r in frames];streamvals=[float(r['streaming_ms']) for r in frames]
 result['originalMeasuredFrameDistribution']=stats(framevals)
 result['originalMeasuredStreamingDistribution']=stats(streamvals)
 result['numericGates']=dict(frameP95=stats(framevals)['p95']<=16.7,frameP99=stats(framevals)['p99']<=25,noFramesGE50=stats(framevals)['framesGE50']==0,streamingP95=stats(streamvals)['p95']<=2,noStreamingFailures=report['streamingFailures']==0)
 return result
data=dict(definition=json.loads((root/'ownership-performance-definition-20261010.json').read_text(encoding='utf-8-sig')),control=run(root/'city-ownership-visible-baseline-100-20261010-144522-ad0f428d'),candidate=run(root/'city-ownership-visible-sidecar-v5-100-20261010-144739-74651758'),limitations=['Visible normal-window1920x1200 no-tracker Midtown100m/s, TAA/vSync1/target60; 15s preparation plus90s measurement. No webcam or image capture.','Original gate uses every measured frames.csv row and nearest-rank percentiles. >=0.5ms streaming subset is diagnostic only and supersedes the prior active-subset publication gate.','FrameTimingManager samples are asynchronous; valid timestamps are deduplicated, unavailable GPU samples omitted, and coverage counts shown. CPU frame includes wait; CPU main/render and present wait are separate.','Realtime intervals measure LateUpdate-to-LateUpdate wall clock; engine unscaled frame_ms is the original gate metric. Timestamp cadence can differ, so both are reported.','Startup means fresh application process; filesystem/driver caches are not forcibly flushed.','Whole managed heap and GPU-driver memory are process/device-wide, not ownership-only memory.','This regional90s no-tracker pair cannot certify all regions/speeds,20-minute memory stability, real webcam accuracy, or original aerial flicker.'])
(out/'comparison.json').write_text(json.dumps(data,indent=2),encoding='utf-8')
for label in ['control','candidate']:
 d=data[label];print(label,json.dumps({k:d[k] for k in ['originalMeasuredFrameDistribution','originalMeasuredStreamingDistribution','numericGates']},indent=2))
 for name,p in d['phases'].items():print(name,json.dumps({k:p[k] for k in ['wallIntervals','cpuFrame','gpuFrame','cpuMain','presentWait','streamingAll','streamingActiveDiagnostic','ownershipWorkDiagnostic','loadedRange','pendingMaximum','failuresMaximum','wholeManagedHeapPeak']},indent=2))
