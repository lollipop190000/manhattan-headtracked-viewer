"""Summarize native fixed-input replays; never label these as PPO evaluation."""
import argparse
import csv
import json
from pathlib import Path
from evaluate_traversal import read_rows,summarize


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('telemetry',type=Path);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    rows=read_rows(args.telemetry)
    grouped={mode:[r for r in rows if r['condition']==mode] for mode in ['off','on']}
    keys=lambda values:[(int(r['seed']),int(r['episode'])) for r in values]
    a,b=keys(grouped['off']),keys(grouped['on'])
    if not rows or len(a)!=len(set(a)) or len(b)!=len(set(b)) or set(a)!=set(b):p.error('Both conditions must contain the same unique seed/episode pairs.')
    result={mode:summarize(values) for mode,values in grouped.items()};result['source']='scripted input timeline; not a trained policy'
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result,indent=2))


if __name__=='__main__':main()
