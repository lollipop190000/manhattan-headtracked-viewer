"""Check the isolated tracker environment and pinned model without a webcam."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import platform


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path,
                        default=root / ".tools/head-tracked-display/python_tracker/face_landmarker.task")
    args = parser.parse_args()
    version = json.loads((root / "tools/technology-version.json").read_text(encoding="utf-8"))
    if platform.python_version() != version["pythonVersion"]:
        raise ValueError(f"Expected Python {version['pythonVersion']}, got {platform.python_version()}")
    lock = root / "tools/tracker-requirements.lock.txt"
    for line in lock.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        distribution, expected = line.strip().split("==", maxsplit=1)
        actual = importlib.metadata.version(distribution)
        if actual != expected:
            raise ValueError(f"Tracker dependency differs from tested lock: {distribution} {actual}, expected {expected}")
    providers = {distribution.metadata["Name"].lower().replace("_", "-")
                 for distribution in importlib.metadata.distributions()
                 if distribution.metadata["Name"].lower().replace("_", "-").startswith("opencv-")}
    if providers != {"opencv-contrib-python"}:
        raise ValueError(f"Use exactly one tested OpenCV provider: {providers}")
    if hashlib.sha256(args.model.read_bytes()).hexdigest() != version["faceModelSha256"]:
        raise ValueError("Face model SHA-256 does not match the pinned model")
    import cv2
    import mediapipe as mp
    import numpy as np
    from mediapipe.tasks.python import BaseOptions, vision
    options = vision.FaceLandmarkerOptions(base_options=BaseOptions(model_asset_path=str(args.model)),
                                           running_mode=vision.RunningMode.IMAGE, num_faces=1)
    with vision.FaceLandmarker.create_from_options(options) as detector:
        result = detector.detect(mp.Image(image_format=mp.ImageFormat.SRGB,
                                          data=np.zeros((480, 640, 3), dtype=np.uint8)))
        if result.face_landmarks:
            raise ValueError("Blank test image unexpectedly reported a face")
    print(f"Tracker model/CPU smoke test passed: Python {platform.python_version()}, "
          f"MediaPipe {mp.__version__}, OpenCV {cv2.__version__}, NumPy {np.__version__}")


if __name__ == "__main__":
    main()
