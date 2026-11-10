#if RISV4_JSON
using System;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    // A small triangle BVH shared by brush picking and closest-surface queries.
    public sealed class KaiFitSurface
    {
        public struct Hit { public Vector3 Point, Normal, Barycentric; public float Distance; public int Triangle; }
        sealed class Node { public Bounds Box; public Node Left, Right; public int Start, Count; }
        Vector3[] vertices, normals;
        readonly int[] triangles, order;
        readonly Node root;
        public KaiFitSurface(Vector3[] points, Vector3[] directions, int[] indices)
        {
            vertices=points; normals=directions; triangles=indices;
            order=new int[indices.Length/3]; for(int i=0;i<order.Length;i++) order[i]=i;
            if(order.Length==0) throw new InvalidOperationException("三角形のあるメッシュを選んでください。");
            root=Build(0,order.Length);
        }
        public void Refit(Vector3[] points,Vector3[] directions)
        {
            if(points.Length!=vertices.Length)throw new InvalidOperationException("頂点数が変更されています。");
            vertices=points;normals=directions;RefitNode(root);
        }
        void RefitNode(Node n)
        {
            if(n.Left!=null)
            {RefitNode(n.Left);RefitNode(n.Right);n.Box=n.Left.Box;n.Box.Encapsulate(n.Right.Box);return;}
            n.Box=new Bounds(vertices[triangles[order[n.Start]*3]],Vector3.zero);
            for(int i=n.Start;i<n.Start+n.Count;i++)for(int j=0;j<3;j++)n.Box.Encapsulate(vertices[triangles[order[i]*3+j]]);
        }
        public bool OutwardDistance(Vector3 point,Vector3 outward,float range,float gap,out float distance)
        {
            distance=0;
            if(outward.sqrMagnitude<.5f)return false;
            outward.Normalize();
            // Search only along the garment's outward direction. Nearest-surface
            // normals can point inward when a scaled breast encloses a garment vertex.
            if(!Raycast(new Ray(point+outward*range,-outward),out var hit))return false;
            if(Vector3.Dot(hit.Normal,outward)<.2f || hit.Distance>range+gap)return false;
            distance=range-hit.Distance+gap;return distance>1e-6f;
        }
        Vector3 Center(int t) => (vertices[triangles[t*3]]+vertices[triangles[t*3+1]]+vertices[triangles[t*3+2]])/3;
        Node Build(int start,int count)
        {
            var n=new Node {Start=start,Count=count,Box=new Bounds(vertices[triangles[order[start]*3]],Vector3.zero)};
            for(int i=start;i<start+count;i++) for(int j=0;j<3;j++) n.Box.Encapsulate(vertices[triangles[order[i]*3+j]]);
            if(count<=12) return n;
            var size=n.Box.size; int axis=size.x>size.y ? (size.x>size.z?0:2):(size.y>size.z?1:2);
            Array.Sort(order,start,count,System.Collections.Generic.Comparer<int>.Create((a,b)=>Center(a)[axis].CompareTo(Center(b)[axis])));
            int half=count/2; n.Left=Build(start,half); n.Right=Build(start+half,count-half); return n;
        }
        Vector3 Normal(int t,Vector3 bary)
        {
            int a=triangles[t*3],b=triangles[t*3+1],c=triangles[t*3+2];
            var normal=normals!=null && normals.Length==vertices.Length ? normals[a]*bary.x+normals[b]*bary.y+normals[c]*bary.z : Vector3.zero;
            return normal.sqrMagnitude>1e-12f?normal.normalized:Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).normalized;
        }
        public bool Nearest(Vector3 point,float range,out Hit hit)
        {
            hit=new Hit {Distance=range}; bool found=false; Search(root,point,ref hit,ref found); return found;
        }
        void Search(Node n,Vector3 point,ref Hit hit,ref bool found)
        {
            if(n.Box.SqrDistance(point)>hit.Distance*hit.Distance) return;
            if(n.Left!=null)
            {
                bool left=n.Left.Box.SqrDistance(point)<n.Right.Box.SqrDistance(point);
                Search(left?n.Left:n.Right,point,ref hit,ref found); Search(left?n.Right:n.Left,point,ref hit,ref found); return;
            }
            for(int i=n.Start;i<n.Start+n.Count;i++)
            {
                int t=order[i]; Vector3 bary;
                var p=Closest(point,vertices[triangles[t*3]],vertices[triangles[t*3+1]],vertices[triangles[t*3+2]],out bary);
                float d=Vector3.Distance(p,point);
                if(d<=hit.Distance) {found=true; hit=new Hit {Point=p,Normal=Normal(t,bary),Distance=d,Triangle=t,Barycentric=bary};}
            }
        }
        public bool Raycast(Ray ray,out Hit hit)
        {
            hit=new Hit {Distance=float.PositiveInfinity}; bool found=false; Cast(root,ray,ref hit,ref found); return found;
        }
        void Cast(Node n,Ray ray,ref Hit hit,ref bool found)
        {
            float near; if(!n.Box.IntersectRay(ray,out near)||near>hit.Distance) return;
            if(n.Left!=null) {Cast(n.Left,ray,ref hit,ref found);Cast(n.Right,ray,ref hit,ref found);return;}
            for(int i=n.Start;i<n.Start+n.Count;i++)
            {
                int t=order[i]; var a=vertices[triangles[t*3]]; var e1=vertices[triangles[t*3+1]]-a; var e2=vertices[triangles[t*3+2]]-a;
                var p=Vector3.Cross(ray.direction,e2); float det=Vector3.Dot(e1,p); if(Mathf.Abs(det)<1e-10f)continue;
                var q=ray.origin-a; float u=Vector3.Dot(q,p)/det; if(u<0||u>1)continue;
                var cross=Vector3.Cross(q,e1); float v=Vector3.Dot(ray.direction,cross)/det;if(v<0||u+v>1)continue;
                float d=Vector3.Dot(e2,cross)/det;if(d<0||d>=hit.Distance)continue;
                var bary=new Vector3(1-u-v,u,v);
                found=true;hit=new Hit {Point=ray.GetPoint(d),Normal=Normal(t,bary),Distance=d,Triangle=t,Barycentric=bary};
            }
        }
        static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b,Vector3 c,out Vector3 w)
        {
            var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0&&d2<=0){w=new Vector3(1,0,0);return a;}
            var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0&&d4<=d3){w=new Vector3(0,1,0);return b;}
            float vc=d1*d4-d3*d2;
            if(vc<=0&&d1>=0&&d3<=0){float v=d1/(d1-d3);w=new Vector3(1-v,v,0);return a+ab*v;}
            var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0&&d5<=d6){w=new Vector3(0,0,1);return c;}
            float vb=d5*d2-d1*d6;
            if(vb<=0&&d2>=0&&d6<=0){float v=d2/(d2-d6);w=new Vector3(1-v,0,v);return a+ac*v;}
            float va=d3*d6-d5*d4;
            if(va<=0&&d4-d3>=0&&d5-d6>=0){float v=(d4-d3)/((d4-d3)+(d5-d6));w=new Vector3(0,1-v,v);return b+(c-b)*v;}
            float sum=va+vb+vc;
            if(Mathf.Abs(sum)<1e-20f){w=new Vector3(1,0,0);return a;}
            w=new Vector3(va,vb,vc)/sum;return a*w.x+b*w.y+c*w.z;
        }
    }
}
#endif
