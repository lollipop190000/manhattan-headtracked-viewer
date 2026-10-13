// System.IO.Ports는 Player Settings > Api Compatibility Level 이 ".NET Framework"일 때만 있다.
// ".NET Standard 2.1"이면 이 파일은 비어서 컴파일되고, 대신 WristUdpReceiver(브리지)를 쓴다.
#if !NET_STANDARD_2_1 && !NET_STANDARD
using System;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

namespace WebShooter.Input
{
    /// <summary>손목 아두이노(USB 또는 HC-05 블루투스 COM 포트)를 직접 읽는다. 연결이 끊기면 계속 재시도한다.</summary>
    public sealed class WristSerialReceiver : WristSource
    {
        [SerializeField] private string portName = "COM9";
        [SerializeField] private int baudRate = 115200;   // USB 115200 / HC-05 9600
        private Thread worker;
        private volatile bool running;
        private volatile string status = "Stopped";

        public override string Status => status;

        private void OnEnable()
        {
            ResetSource();
            running = true;
            worker = new Thread(ReadLoop) { IsBackground = true, Name = "Wrist serial" };
            worker.Start();
        }

        private void OnDisable()
        {
            running = false;
            if (worker != null && worker.IsAlive) worker.Join(800);
            status = "Stopped";
        }

        private void ReadLoop()
        {
            while (running)
            {
                try
                {
                    status = "Opening " + portName;
                    using (var port = new SerialPort(portName, baudRate) { ReadTimeout = 200, NewLine = "\n" })
                    {
                        port.DtrEnable = false;   // 열 때 아두이노가 리셋(자이로 재보정)되지 않게
                        port.RtsEnable = false;
                        port.Open();
                        status = "Connected " + portName;
                        while (running)
                        {
                            try { Publish(port.ReadLine()); }
                            catch (TimeoutException) { /* 데이터 없음. 계속 대기 */ }
                        }
                    }
                }
                catch (Exception ex) when (running)
                {
                    status = "Disconnected (" + ex.GetType().Name + ")";
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
#endif
