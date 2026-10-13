using System;
using System.IO;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>Settings orchestration for the companion tracker's session-scoped webcam control.</summary>
    public sealed class ViewerCameraSelection
    {
        [Serializable] public sealed class Device
        {
            public string id, name, backend, path;
            public int index;
        }
        [Serializable] public sealed class Snapshot
        {
            public string session, status, error;
            public double updatedUnix;
            public Device selected;
            public Device[] devices = Array.Empty<Device>();
            public int frameWidth, frameHeight;
            public bool blankFrames;
        }
        [Serializable] private sealed class Request
        {
            public string session, revision, action, deviceId;
        }
        private readonly string directory;
        public Snapshot Current { get; private set; }
        public string Error { get; private set; }
        public bool Online => Current != null && !string.IsNullOrEmpty(Current.session) &&
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - Current.updatedUnix < 5 &&
            Current.status != "Tracker stopped";

        public ViewerCameraSelection(string directory) { this.directory = directory; }

        public void ReadStatus()
        {
            try
            {
                string path = Path.Combine(directory, "tracker_camera_status.json");
                if (!File.Exists(path)) { Current = null; return; }
                Current = JsonUtility.FromJson<Snapshot>(File.ReadAllText(path));
            }
            catch (Exception error) { Current = null; Error = "Could not read camera status: " + error.Message; }
        }

        public bool Refresh() => Send("refresh", null);
        public bool Select(Device device) => device != null && Send("select", device.id);
        private bool Send(string action, string deviceId)
        {
            if (!Online) { Error = "Start the Python tracker to choose a webcam."; return false; }
            try
            {
                string path = Path.Combine(directory, "tracker_camera_request.json");
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(new Request {
                    session = Current.session, revision = Guid.NewGuid().ToString("N"), action = action, deviceId = deviceId }));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
                Error = null;
                return true;
            }
            catch (Exception error) { Error = "Could not request camera: " + error.Message; return false; }
        }

        public string Describe()
        {
            if (!Online) return Error ?? "Webcam selection needs the Python tracker. Start with tools/run.ps1.";
            var device = Current.selected;
            string label = device != null ? device.name + " (" + device.backend + ", " + device.index + ")" : "No camera";
            return label + "\n" + Current.status +
                (Current.frameWidth > 0 ? "  " + Current.frameWidth + " x " + Current.frameHeight : "") +
                (!string.IsNullOrEmpty(Current.error) ? "\n" + Current.error : "") +
                (!string.IsNullOrEmpty(Error) ? "\n" + Error : "");
        }
    }
}
