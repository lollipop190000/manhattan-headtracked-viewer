import json, pathlib, subprocess, hashlib, re
import numpy as np
from PIL import Image
import imageio_ffmpeg
import cv2

root=pathlib.Path(__file__).resolve().parent
out=pathlib.Path(__import__('sys').argv[1]).resolve()
report=json.loads((out/'capture-report.json').read_text(encoding='utf-8-sig'))
motion=[s for s in report['samples'] if s['kind']=='motion']
assert len(motion)>1
times=[s['realtime'] for s in motion]
assert all(b>a for a,b in zip(times,times[1:]))
concat=out/'frames.ffconcat'
lines=['ffconcat version 1.0']
for i,s in enumerate(motion):
    assert (out/s['file']).exists()
    lines += ["file '"+s['file']+"'",'option framerate 1000']
    if i+1<len(motion): lines.append(f'duration {times[i+1]-times[i]:.9f}')
concat.write_text('\n'.join(lines)+'\n',encoding='utf-8')
movie=out/'current-candidate-rooftop-motion.mp4'
assert not movie.exists(), 'Existing movie preserved'
ff=imageio_ffmpeg.get_ffmpeg_exe()
args=[ff,'-hide_banner','-f','concat','-safe','0','-i',str(concat),'-vf','vflip','-fps_mode','vfr','-c:v','libx264','-preset','veryfast','-crf','18','-pix_fmt','yuv420p','-video_track_timescale','1000000','-threads','4','-movflags','+faststart',str(movie)]
p=subprocess.run(args,capture_output=True,text=True)
(out/'encode.log').write_text(p.stderr,encoding='utf-8')
assert p.returncode==0,p.stderr[-3000:]
q=subprocess.run([ff,'-hide_banner','-i',str(movie),'-vf','showinfo','-f','null','NUL'],capture_output=True,text=True)
(out/'decode-qa.log').write_text(q.stderr,encoding='utf-8')
assert q.returncode==0
pts=[float(x) for x in re.findall(r'\bn:\s*\d+\s+pts:\s*\d+\s+pts_time:([0-9.]+)',q.stderr)]
assert len(pts)==len(motion),(len(pts),len(motion))
assert all(b>a for a,b in zip(pts,pts[1:]))
assert max(abs(a-(b-times[0])) for a,b in zip(pts,times))<.002
orient={}
for name in ['street','roof','route-end']:
    native=np.array(Image.open(out/('native-'+name+'.png')).convert('RGB'))
    raw=np.array(Image.open(out/(name+'.png')).convert('RGB'))
    assert native.shape==raw.shape==(1200,1920,3)
    orient[name]={'same_direction_mad':float(np.abs(native.astype(float)-raw).mean()),'flipped_mad':float(np.abs(native.astype(float)-raw[::-1]).mean())}
    assert orient[name]['flipped_mad']<.01
cap=cv2.VideoCapture(str(movie))
assert cap.isOpened()
assert int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))==1920 and int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))==1200
selected={0:'first',len(motion)//2:'middle',len(motion)-1:'last'}
decoded=0
while True:
    ok,frame=cap.read()
    if not ok:break
    if decoded in selected:
        cv2.imwrite(str(out/('video-qa-'+selected[decoded]+'.png')),frame)
    decoded+=1
cap.release()
assert decoded==len(motion)
qa={'actualPlayerCapture':True,'desktopOrFaceCaptured':False,'width':1920,'height':1200,'frameCount':len(motion),'requestedFps':report['requestedCaptureFps'],'actualMeanCaptureFps':report['observedCaptureFps'],'firstToLastSeconds':times[-1]-times[0],'encodedFirstToLastSeconds':pts[-1]-pts[0],'variableFrameRate':True,'timestampMaxErrorSeconds':max(abs(a-(b-times[0])) for a,b in zip(pts,times)),'transform':'Vertical coordinate orientation correction only; native Unity PNG and flipped raw render texture match exactly. No enhancement, trimming, interpolation, or generated frames.','orientation':orient,'firstPose':motion[0]['city'],'lastPose':motion[-1]['city'],'maxPending':max(s['pending'] for s in motion),'maxFailures':max(s['failures'] for s in motion),'firstOrigin':motion[0]['origin'],'lastOrigin':motion[-1]['origin'],'frameReadbackWriteMaxMs':1000*max(s['readbackWriteSeconds'] for s in motion),'stillReadbackWriteMs':[1000*s['readbackWriteSeconds'] for s in report['samples'] if s['kind']=='still'],'performanceAcceptance':False,'movieSha256':hashlib.sha256(movie.read_bytes()).hexdigest(),'movieBytes':movie.stat().st_size,'ffmpegArgs':args}
(out/'encoding-qa.json').write_text(json.dumps(qa,indent=2),encoding='utf-8')
print(json.dumps(qa,indent=2))
