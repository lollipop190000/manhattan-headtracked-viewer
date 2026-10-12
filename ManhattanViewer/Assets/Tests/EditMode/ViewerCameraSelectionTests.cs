using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Manhattan.Viewer.Tests
{
    public sealed class ViewerCameraSelectionTests
    {
        private string directory;
        [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "viewer-camera-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
        [TearDown] public void TearDown() { Directory.Delete(directory, true); }

        [Test] public void SelectionUsesCurrentTrackerSessionAndLeavesPhysicalCalibrationUntouched()
        {
            var device = new ViewerCameraSelection.Device { id = "eshare-dshow", name = "EShare Virtual Camera", index = 1, backend = "dshow" };
            var status = new ViewerCameraSelection.Snapshot { session = "live-session", updatedUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                selected = device, devices = new[] {device}, status = "Receiving camera frames" };
            File.WriteAllText(Path.Combine(directory, "tracker_camera_status.json"), JsonUtility.ToJson(status));
            string calibration = Path.Combine(directory, "physical-calibration.json"); File.WriteAllText(calibration, "original physical measurements");
            var selection = new ViewerCameraSelection(directory); selection.ReadStatus();
            Assert.That(selection.Online, Is.True); Assert.That(selection.Select(device), Is.True);
            string request = File.ReadAllText(Path.Combine(directory, "tracker_camera_request.json"));
            Assert.That(request, Does.Contain("live-session")); Assert.That(request, Does.Contain("eshare-dshow"));
            Assert.That(File.ReadAllText(calibration), Is.EqualTo("original physical measurements"));
            Assert.That(File.Exists(Path.Combine(directory, "tracker_camera_preference.json")), Is.False, "Only the tracker confirms and saves a successful selection.");
        }

        [Test] public void OldOrStoppedTrackerCannotReceiveSelection()
        {
            var status = new ViewerCameraSelection.Snapshot { session = "old", updatedUnix = 1, status = "Receiving camera frames" };
            string path = Path.Combine(directory, "tracker_camera_status.json"); File.WriteAllText(path, JsonUtility.ToJson(status));
            var selection = new ViewerCameraSelection(directory); selection.ReadStatus();
            Assert.That(selection.Refresh(), Is.False); Assert.That(File.Exists(Path.Combine(directory, "tracker_camera_request.json")), Is.False);
            status.updatedUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0; status.status = "Tracker stopped";
            File.WriteAllText(path, JsonUtility.ToJson(status)); selection.ReadStatus(); Assert.That(selection.Online, Is.False);
        }

        [Test] public void BlackVideoStatusRemainsVisibleAndMalformedStatusDoesNotThrow()
        {
            var status = new ViewerCameraSelection.Snapshot { session = "live", updatedUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                status = "Black video: connect the source in EShare / camera app", blankFrames = true };
            string path = Path.Combine(directory, "tracker_camera_status.json"); File.WriteAllText(path, JsonUtility.ToJson(status));
            var selection = new ViewerCameraSelection(directory); selection.ReadStatus(); Assert.That(selection.Describe(), Does.Contain("Black video"));
            File.WriteAllText(path, "broken JSON"); Assert.DoesNotThrow(selection.ReadStatus);
        }
    }
}
