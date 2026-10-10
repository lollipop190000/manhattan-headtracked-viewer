import json,pathlib,subprocess,re,hashlib
import numpy as np
from PIL import Image
import imageio_ffmpeg,cv2
root=pathlib.Path(__file__).resolve().parent;p=pathlib.Path(__import__('sys').argv[1]).resolve()
d=json.loads((p/'capture-report.json').read_text());m=[s for s in d['samples'] if s['kind']=='motion'];times=[s['realtime'] for s in m];assert d['readbackErrors']==0 and d['schedulingSkipped']==0
a=np.asarray(Image.open(p/'anchor.ppm')).astype(float);b=np.asarray(Image.open(p/'native-anchor.png')).astype(float);same=float(np.abs(a-b).mean());flip=float(np.abs(a[::-1]-b).mean());assert min(same,flip)<.01;vf='vflip' if flip<same else None
lines=['ffconcat version 1.0']
for i,s in enumerate(m):
    assert (p/s['file']).exists();lines.extend(["file '"+s['file']+"'",'option framerate 1000'])
    if i+1<len(m):lines.append(f'duration {times[i+1]-times[i]:.9f}')
(p/'frames.ffconcat').write_text('\n'.join(lines)+'\n')
ff=imageio_ffmpeg.get_ffmpeg_exe();movie=p/'cohort-cost-rooftop-roundtrip.mp4';assert not movie.exists();args=[ff,'-hide_banner','-f','concat','-safe','0','-i',str(p/'frames.ffconcat')]
if vf:args+=['-vf',vf]
args+=['-fps_mode','vfr','-c:v','libx264','-preset','veryfast','-crf','18','-pix_fmt','yuv420p','-video_track_timescale','1000000','-threads','4','-movflags','+faststart',str(movie)]
r=subprocess.run(args,capture_output=True,text=True);(p/'encode.log').write_text(r.stderr);assert r.returncode==0
r=subprocess.run([ff,'-hide_banner','-i',str(movie),'-vf','showinfo','-f','null','NUL'],capture_output=True,text=True);(p/'decode-qa.log').write_text(r.stderr);assert r.returncode==0
pts=[float(x) for x in re.findall(r'\bn:\s*\d+\s+pts:\s*\d+\s+pts_time:([0-9.]+)',r.stderr)];assert len(pts)==len(m);error=max(abs(a-(b-times[0])) for a,b in zip(pts,times));assert error<.002
cap=cv2.VideoCapture(str(movie));assert cap.isOpened() and int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))==1920 and int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))==1200
selected={int(i*(len(m)-1)/8):str(i) for i in range(9)};n=0
while True:
 ok,frame=cap.read()
 if not ok:break
 if n in selected:cv2.imwrite(str(p/('video-qa-'+selected[n]+'.png')),frame)
 n+=1
cap.release();assert n==len(m)
qa={'frames':n,'width':1920,'height':1200,'firstToLastSeconds':times[-1]-times[0],'actualMeanCaptureFps':d['observedCaptureFps'],'requestedFps':30,'timestampMaxErrorSeconds':error,'orientationAnchorSameMAD':same,'orientationAnchorFlippedMAD':flip,'encodingOrientationTransform':vf or 'none','variableFrameRate':True,'noInterpolationTrimOrAcceleration':True,'maxRequestIssueMs':max(s['requestIssueMs'] for s in m),'maxDataCopyMs':max(s['copyMs'] for s in m),'maxBackgroundWriteMs':max(s['backgroundWriteMs'] for s in m),'readbackErrors':d['readbackErrors'],'schedulingSkipped':d['schedulingSkipped'],'maxPending':max(s['pending'] for s in m),'maxFailures':max(s['failures'] for s in m),'firstPose':m[0]['city'],'lastPose':m[-1]['city'],'runtimeSourceSha256':d['candidateRuntimeSourceSha256'],'movieSha256':hashlib.sha256(movie.read_bytes()).hexdigest(),'movieBytes':movie.stat().st_size,'performanceAcceptance':False}
(p/'encoding-qa.json').write_text(json.dumps(qa,indent=2));print(json.dumps(qa,indent=2))
