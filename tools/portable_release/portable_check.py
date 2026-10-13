"""Check the shipped interpreter, dependencies and face model without opening a webcam."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import platform
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--list-cameras', action='store_true')
    args = parser.parse_args()
    tracker = Path(__file__).resolve().parents[1] / '.runtime/tracker'
    manifest = json.loads((tracker/'portable-tracker.json').read_text(encoding='utf-8'))
    assert platform.python_version() == manifest['pythonVersion'], 'Bundled Python version differs'
    assert sys.flags.isolated and sys.flags.ignore_environment and sys.flags.no_site, 'Python is not isolated'
    assert all(Path(path).resolve().is_relative_to(tracker.resolve()) for path in sys.path), 'External Python packages are on the path'
    for requirement in (tracker/'tracker-requirements.lock.txt').read_text(encoding='utf-8').splitlines():
        if not requirement.strip() or requirement.startswith('#'): continue
        name, expected = requirement.split('==')
        assert importlib.metadata.version(name) == expected, 'Bundled dependency differs: ' + name
    assert hashlib.sha256((tracker/'face_landmarker.task').read_bytes()).hexdigest() == manifest['modelSha256'], 'Face model checksum differs'
    import cv2
    import mediapipe as mp
    import numpy as np
    from mediapipe.tasks.python import BaseOptions, vision
    from pose_estimator import PoseEstimator
    options = vision.FaceLandmarkerOptions(base_options=BaseOptions(model_asset_buffer=(tracker/'face_landmarker.task').read_bytes()),
                                           running_mode=vision.RunningMode.IMAGE, num_faces=1)
    with vision.FaceLandmarker.create_from_options(options) as detector:
        result = detector.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.zeros((480,640,3),dtype=np.uint8)))
        assert not result.face_landmarks
    assert PoseEstimator().canonical.shape[1] == 3
    print('Bundled webcam tracker is ready. No system Python or pip installation is required.')
    if args.list_cameras:
        from camera_control import discover_cameras
        devices = discover_cameras()
        if not devices: print('No webcam is listed. Connect a webcam and enable Windows camera access for desktop apps.')
        for device in devices: print(f"Camera {device['index']} ({device['backend']}): {device['name']}")


if __name__ == '__main__': main()
