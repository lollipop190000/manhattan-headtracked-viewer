#!/usr/bin/env python3
"""장치 없이 Unity를 테스트하기 위한 가짜 손목 송신기. 펌웨어와 같은 "yaw,pitch,fire" 줄을 UDP로 보낸다.

손이 천천히 좌우/상하로 흔들리고, --fire-every 초마다 발사(fire=1), --recenter-at 초에 재보정(fire=2)을 보낸다.
  python tools/fake_wrist.py
  python tools/fake_wrist.py --fire-every 2 --recenter-at 20
"""
import argparse
import math
import socket
import time


def main():
    ap = argparse.ArgumentParser(description="Fake wrist device (UDP)")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8766)
    ap.add_argument("--rate", type=float, default=50.0, help="초당 전송 횟수 (펌웨어 USB 모드: 50)")
    ap.add_argument("--yaw-amp", type=float, default=20.0, help="좌우 흔들림 진폭(도)")
    ap.add_argument("--pitch-amp", type=float, default=12.0, help="상하 흔들림 진폭(도)")
    ap.add_argument("--fire-every", type=float, default=3.0, help="이 간격(초)마다 fire=1 (0이면 없음)")
    ap.add_argument("--recenter-at", type=float, default=0.0, help="이 시각(초)에 fire=2 한 번 (0이면 없음)")
    args = ap.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    dest = (args.host, args.port)
    t0 = time.time()
    next_fire = args.fire_every
    recentered = False
    dt = 1.0 / args.rate
    print(f"fake wrist -> udp://{dest[0]}:{dest[1]}  (Ctrl+C로 종료)")
    try:
        while True:
            t = time.time() - t0
            yaw = args.yaw_amp * math.sin(t * 0.8)
            pitch = args.pitch_amp * math.sin(t * 0.55)
            fire = 0
            if args.fire_every > 0 and t >= next_fire:
                fire, next_fire = 1, next_fire + args.fire_every
            if args.recenter_at > 0 and not recentered and t >= args.recenter_at:
                fire, recentered = 2, True
            sock.sendto(f"{yaw:.1f},{pitch:.1f},{fire}\n".encode(), dest)
            time.sleep(dt)
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
