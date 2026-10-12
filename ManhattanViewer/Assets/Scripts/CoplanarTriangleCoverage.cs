using System;
using System.Collections.Generic;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>
    /// Offline proof that a triangle is covered by the union of opaque, consistently
    /// wound provider triangles. Different edge subdivisions and diagonals are allowed.
    /// No imported vertices are snapped, moved or rewritten. Unproved fragments survive.
    /// </summary>
    public sealed class CoplanarTriangleCoverage
    {
        const double Tolerance = .001;
        const double ToleranceSquared = Tolerance * Tolerance;
        const double CellSize = 32;
        readonly Dictionary<(int, int), List<Face>> cells = new Dictionary<(int, int), List<Face>>();
        readonly List<Face> largeFaces = new List<Face>();

        sealed class Face
        {
            public V3 a, b, c, normal, min, max;
            public Face(Vector3 a, Vector3 b, Vector3 c)
            {
                this.a = new V3(a); this.b = new V3(b); this.c = new V3(c);
                normal = V3.Cross(this.b - this.a, this.c - this.a);
                min = V3.Min(this.a, V3.Min(this.b, this.c));
                max = V3.Max(this.a, V3.Max(this.b, this.c));
            }
        }
        readonly struct V3
        {
            public readonly double x, y, z;
            public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public V3(Vector3 p) : this(p.x, p.y, p.z) { }
            public static V3 operator +(V3 a, V3 b) => new V3(a.x+b.x,a.y+b.y,a.z+b.z);
            public static V3 operator -(V3 a, V3 b) => new V3(a.x-b.x,a.y-b.y,a.z-b.z);
            public static V3 operator *(V3 a, double s) => new V3(a.x*s,a.y*s,a.z*s);
            public static double Dot(V3 a,V3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
            public static V3 Cross(V3 a,V3 b) => new V3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
            public static V3 Min(V3 a,V3 b) => new V3(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z));
            public static V3 Max(V3 a,V3 b) => new V3(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z));
            public double Squared => Dot(this,this);
        }
        readonly struct P2
        {
            public readonly double x, y;
            public P2(double x,double y) { this.x=x;this.y=y; }
        }
        sealed class Projected
        {
            public Face face;
            public P2[] points;
        }
        static int Cell(double value) => checked((int)Math.Floor(value / CellSize));
        public void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            var face = new Face(a,b,c);
            if (!(face.normal.Squared > 1e-20)) return;
            int x0=Cell(face.min.x-Tolerance),x1=Cell(face.max.x+Tolerance);
            int z0=Cell(face.min.z-Tolerance),z1=Cell(face.max.z+Tolerance);
            if ((long)(x1-x0+1)*(z1-z0+1)>4096) { largeFaces.Add(face);return; }
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
            {
                if(!cells.TryGetValue((x,z),out var list))cells.Add((x,z),list=new List<Face>());
                list.Add(face);
            }
        }
        public bool Covers(Vector3 a, Vector3 b, Vector3 c)
        {
            var target=new Face(a,b,c);
            double length=Math.Sqrt(target.normal.Squared);
            if(!(length>1e-10))return false;
            V3 n=target.normal*(1/length),u=(target.b-target.a)*(1/Math.Sqrt((target.b-target.a).Squared)),v=V3.Cross(n,u);
            P2 Project(V3 p) { var d=p-target.a;return new P2(V3.Dot(d,u),V3.Dot(d,v)); }
            var candidates=new HashSet<Face>(largeFaces);
            int x0=Cell(target.min.x-Tolerance),x1=Cell(target.max.x+Tolerance);
            int z0=Cell(target.min.z-Tolerance),z1=Cell(target.max.z+Tolerance);
            if((long)(x1-x0+1)*(z1-z0+1)>4096)return false;
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
                if(cells.TryGetValue((x,z),out var list))foreach(var face in list)candidates.Add(face);
            var projected=new List<Projected>();
            foreach(var face in candidates)
            {
                if(face.max.x<target.min.x-Tolerance||face.min.x>target.max.x+Tolerance||
                   face.max.y<target.min.y-Tolerance||face.min.y>target.max.y+Tolerance||
                   face.max.z<target.min.z-Tolerance||face.min.z>target.max.z+Tolerance||
                   V3.Dot(n,face.normal)<=0)continue;
                // Test the bounded overlap below. A long provider can extend beyond
                // the plane of a short rounded hull edge while covering that edge
                // everywhere within 1mm; testing its remote vertices rejects it.
                double pa=V3.Dot(face.a-target.a,n),pb=V3.Dot(face.b-target.a,n),pc=V3.Dot(face.c-target.a,n);
                if(Math.Min(pa,Math.Min(pb,pc))>Tolerance+1e-9||Math.Max(pa,Math.Max(pb,pc))<-Tolerance-1e-9)continue;
                double normalLength=Math.Sqrt(face.normal.Squared);
                double da=V3.Dot(target.a-face.a,face.normal)/normalLength;
                double db=V3.Dot(target.b-face.a,face.normal)/normalLength;
                double dc=V3.Dot(target.c-face.a,face.normal)/normalLength;
                if(Math.Min(da,Math.Min(db,dc))>Tolerance||Math.Max(da,Math.Max(db,dc))<-Tolerance)continue;
                projected.Add(new Projected {face=face,points=new[]{Project(face.a),Project(face.b),Project(face.c)}});
            }
            if(projected.Count==0)return false;
            var remaining=new List<List<P2>> {new List<P2> {Project(target.a),Project(target.b),Project(target.c)}};
            foreach(var cover in projected)
            {
                var next=new List<List<P2>>();
                foreach(var piece in remaining)Subtract(piece,cover,target.a,u,v,next);
                remaining=next;
                if(remaining.Count==0)return true;
                // Pathological data fails conservatively instead of unbounded fragmentation.
                if(remaining.Count>512)return false;
            }
            // Independent imports can differ by sub-millimetre rounding. Every residual
            // convex fragment must lie within 1mm (Euclidean 3D) of ONE provider triangle.
            // Distance to a convex triangle is convex, so checking all fragment vertices
            // bounds the entire fragment. Merely sampling a centre or comparing area would
            // wrongly accept holes, overlap counted twice, or a shifted diagonal corner.
            foreach(var piece in remaining)
            {
                bool near=false;
                foreach(var cover in projected)
                {
                    if(WithinTolerance(piece,cover.face,target.a,u,v)){near=true;break;}
                }
                if(!near)return false;
            }
            return true;
        }
        static double Side(P2 p,P2 a,P2 b) => (b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);
        static List<P2> Clip(List<P2> polygon,P2 a,P2 b,bool inside)
        {
            var result=new List<P2>();
            if(polygon.Count==0)return result;
            P2 prev=polygon[polygon.Count-1];double prevSide=Side(prev,a,b);
            foreach(var current in polygon)
            {
                double currentSide=Side(current,a,b);
                bool prevIn=inside?prevSide>=0:prevSide<=0,currentIn=inside?currentSide>=0:currentSide<=0;
                if(prevIn!=currentIn)
                {
                    double t=prevSide/(prevSide-currentSide);
                    result.Add(new P2(prev.x+(current.x-prev.x)*t,prev.y+(current.y-prev.y)*t));
                }
                if(currentIn)result.Add(current);
                prev=current;prevSide=currentSide;
            }
            return result;
        }
        static bool WithinTolerance(List<P2> polygon,Face face,V3 origin,V3 u,V3 v)
        {
            foreach(var p in polygon)
                if(DistanceSquared(origin+u*p.x+v*p.y,face)>ToleranceSquared+1e-15)return false;
            return true;
        }
        static void Subtract(List<P2> polygon,Projected cover,V3 origin,V3 u,V3 v,List<List<P2>> output)
        {
            var triangle=cover.points;
            var remainder=polygon;
            var outsidePieces=new List<List<P2>>();
            for(int edge=0;edge<3&&remainder.Count>=3;edge++)
            {
                var outside=Clip(remainder,triangle[edge],triangle[(edge+1)%3],false);
                if(Area(outside)>1e-12)outsidePieces.Add(outside);
                remainder=Clip(remainder,triangle[edge],triangle[(edge+1)%3],true);
            }
            // Projected overlap alone cannot prove depth coverage. Convex distance
            // bounds every point of this intersection by its vertices, including
            // small angular differences introduced by independent import rounding.
            // An empty intersection is a no-op. Keeping its outside partitions would
            // accumulate arbitrary cuts from unrelated faces and exhaust the guard.
            if(Area(remainder)<=1e-12||!WithinTolerance(remainder,cover.face,origin,u,v))
            {output.Add(polygon);return;}
            output.AddRange(outsidePieces);
        }
        static double Area(List<P2> points)
        {
            double sum=0;
            for(int i=1;i+1<points.Count;i++)sum+=Side(points[i+1],points[0],points[i]);
            return Math.Abs(sum)*.5;
        }
        static double DistanceSquared(V3 p,Face f)
        {
            V3 ab=f.b-f.a,ac=f.c-f.a,ap=p-f.a;
            double d1=V3.Dot(ab,ap),d2=V3.Dot(ac,ap);
            if(d1<=0&&d2<=0)return ap.Squared;
            V3 bp=p-f.b;double d3=V3.Dot(ab,bp),d4=V3.Dot(ac,bp);
            if(d3>=0&&d4<=d3)return bp.Squared;
            double vc=d1*d4-d3*d2;
            if(vc<=0&&d1>=0&&d3<=0)return(p-(f.a+ab*(d1/(d1-d3)))).Squared;
            V3 cp=p-f.c;double d5=V3.Dot(ab,cp),d6=V3.Dot(ac,cp);
            if(d6>=0&&d5<=d6)return cp.Squared;
            double vb=d5*d2-d1*d6;
            if(vb<=0&&d2>=0&&d6<=0)return(p-(f.a+ac*(d2/(d2-d6)))).Squared;
            double va=d3*d6-d5*d4;
            if(va<=0&&d4-d3>=0&&d5-d6>=0)return(p-(f.b+(f.c-f.b)*((d4-d3)/((d4-d3)+(d5-d6))))).Squared;
            double inverse=1/(va+vb+vc);
            return(p-(f.a+ab*(vb*inverse)+ac*(vc*inverse))).Squared;
        }
    }
}
