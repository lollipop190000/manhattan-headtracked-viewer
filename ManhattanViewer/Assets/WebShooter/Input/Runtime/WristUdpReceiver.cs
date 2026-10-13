using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace WebShooter.Input
{
    /// <summary>
    /// 같은 PC의 브리지(tools/wrist_bridge.py)가 보내는 UDP 줄을 받는다. 시리얼 API 설정과 무관하게 동작하고,
    /// 장치 없이 tools/fake_wrist.py로도 테스트할 수 있다. 머리 추적용 TCP 8765와는 별개의 포트를 쓴다.
    /// </summary>
    public sealed class WristUdpReceiver : WristSource
    {
        [SerializeField, Range(1024, 65535)] private int port = 8766;
        private UdpClient client;
        private Thread worker;
        private volatile bool running;
        private volatile string status = "Stopped";

        public override string Status => status;

        private void OnEnable()
        {
            ResetSource();
            running = true;
            worker = new Thread(ReceiveLoop) { IsBackground = true, Name = "Wrist UDP" };
            worker.Start();
        }

        private void OnDisable()
        {
            running = false;
            try { client?.Close(); } catch { /* 이미 닫힌 소켓 */ }
            if (worker != null && worker.IsAlive) worker.Join(500);
            status = "Stopped";
        }

        private void ReceiveLoop()
        {
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                status = $"Listening on 127.0.0.1:{port}";
                var remote = new IPEndPoint(IPAddress.Any, 0);
                while (running)
                {
                    byte[] data = client.Receive(ref remote);
                    foreach (string line in Encoding.ASCII.GetString(data).Split('\n')) Publish(line);
                }
            }
            catch (SocketException ex) { if (running) status = "UDP error: " + ex.Message; }
            catch (ObjectDisposedException) { /* OnDisable에서 닫음 */ }
        }
    }
}
