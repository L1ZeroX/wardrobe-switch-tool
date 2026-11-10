#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    // Constraints are anchored to the starting triangles, not to a newly nearest
    // garment after every stroke. Thus a crossing cannot reverse the layer order.
    public sealed class KaiFitLayers
    {
        struct Anchor
        {
            public int A,B,C; public Vector3 Weights;
            public Vector3 Point(Vector3[] points)=>points[A]*Weights.x+points[B]*Weights.y+points[C]*Weights.z;
            public bool Active(bool[] active)=>active[A]||active[B]||active[C];
        }
        struct Link { public int Inner,Outer; public Anchor Inside,Outside; public Vector3 Normal; public float Separation; }
        readonly Vector3[][] reference,normals;
        readonly int[][] triangles;
        readonly KaiFitSurface[] surfaces;
        readonly List<Link> links=new List<Link>();
        public int[] Order {get;private set;}
        public int ConstraintCount=>links.Count;
        const float Range=.05f;
        public KaiFitLayers(Vector3[][] points,Vector3[][] directions,int[][] indices)
        {
            reference=points;normals=directions;triangles=indices;
            surfaces=points.Select((p,i)=>new KaiFitSurface(p,directions[i],indices[i])).ToArray();
            SetOrder(DetectOrder());
        }
        public int[] DetectOrder()
        {
            var edges=new List<Tuple<int,int,float>>();
            for(int a=0;a<reference.Length;a++)for(int b=a+1;b<reference.Length;b++)
            {
                int positive=0,negative=0;
                for(int side=0;side<2;side++)
                {
                    int source=side==0?a:b,target=side==0?b:a;
                    int step=Math.Max(1,reference[source].Length/512);
                    for(int i=0;i<reference[source].Length;i+=step)
                    {
                        if(!surfaces[target].Nearest(reference[source][i],Range,out var hit)||Vector3.Dot(normals[source][i],hit.Normal)<.5f)continue;
                        float signed=Vector3.Dot(reference[source][i]-hit.Point,normals[source][i])*(side==0?1:-1);
                        if(signed>.0001f)positive++;else if(signed<-.0001f)negative++;
                    }
                }
                int total=positive+negative;
                if(total<4)continue;
                float vote=(positive-negative)/(float)total;
                if(Mathf.Abs(vote)<.2f)continue;
                edges.Add(Tuple.Create(vote>0?b:a,vote>0?a:b,Mathf.Abs(vote)*Mathf.Min(total,64)));
            }
            // A sum of per-mesh votes confuses a corset overlapped by many unrelated
            // accessories. Preserve strong pairwise relations; reject cyclic guesses.
            var before=new bool[reference.Length,reference.Length];
            foreach(var edge in edges.OrderByDescending(e=>e.Item3))
            {
                int a=edge.Item1,b=edge.Item2;if(before[b,a])continue;
                before[a,b]=true;
                for(int k=0;k<reference.Length;k++)for(int i=0;i<reference.Length;i++)for(int j=0;j<reference.Length;j++)
                    if(before[i,k]&&before[k,j])before[i,j]=true;
            }
            var pending=Enumerable.Range(0,reference.Length).ToList();var result=new List<int>();
            while(pending.Count>0)
            {int next=pending.First(i=>!pending.Any(j=>before[j,i]));result.Add(next);pending.Remove(next);}
            return result.ToArray();
        }
        static Anchor Vertex(int i)=>new Anchor {A=i,B=i,C=i,Weights=Vector3.right};
        Anchor Face(int mesh,KaiFitSurface.Hit hit)
        {
            int t=hit.Triangle*3;
            return new Anchor {A=triangles[mesh][t],B=triangles[mesh][t+1],C=triangles[mesh][t+2],Weights=hit.Barycentric};
        }
        public void SetOrder(int[] order)
        {
            if(order==null||order.Length!=reference.Length||!order.OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,reference.Length)))
                throw new InvalidOperationException("重ね着の順番にすべての対象メッシュを1回ずつ指定してください。");
            Order=(int[])order.Clone();links.Clear();
            for(int o=1;o<Order.Length;o++)for(int k=0;k<o;k++)
            {
                int outer=Order[o],inner=Order[k];
                // Both directions protect a dense inner garment under a coarse outer
                // triangle as well as outer vertices lying inside the inner garment.
                for(int side=0;side<2;side++)
                {
                    int source=side==0?outer:inner,target=side==0?inner:outer;
                    for(int v=0;v<reference[source].Length;v++)
                    {
                        if(!surfaces[target].Nearest(reference[source][v],Range,out var hit)||Vector3.Dot(normals[source][v],hit.Normal)<.5f)continue;
                        var outside=side==0?Vertex(v):Face(outer,hit);
                        var inside=side==0?Face(inner,hit):Vertex(v);
                        var normal=side==0?normals[outer][v]:hit.Normal;
                        var diff=outside.Point(reference[outer])-inside.Point(reference[inner]);
                        float separation=Vector3.Dot(diff,normal);
                        // Mesh-wide ordering can be reversed locally at seams or belts.
                        // Allow only a shallow (1 mm) starting overlap; do not pull a
                        // deliberately tucked-in accessory through the covering fabric.
                        if((diff-normal*separation).sqrMagnitude>.0001f||separation<-.001f)continue;
                        links.Add(new Link {Inner=inner,Outer=outer,Inside=inside,Outside=outside,Normal=normal,Separation=Mathf.Max(0,separation)});
                    }
                }
            }
        }
        public int Resolve(Vector3[][] points,bool[][] affected,float gap,bool[][] allowed=null)
        {
            if(gap<0||gap>.01f||float.IsNaN(gap)||float.IsInfinity(gap))throw new InvalidOperationException("衣装同士の隙間は0〜10mmで指定してください。");
            int count=0;
            // Resolve in inner-to-outer order. A triangle displacement may extend just
            // outside the brush: its whole face must clear the inner vertices beneath it.
            foreach(int outer in Order)
            {
            for(int pass=0;pass<3;pass++)foreach(var link in links)
            {
                if(link.Outer!=outer||(!link.Inside.Active(affected[link.Inner])&&!link.Outside.Active(affected[outer])))continue;
                var anchor=link.Outside;
                if(allowed!=null&&(!allowed[outer][anchor.A]||!allowed[outer][anchor.B]||!allowed[outer][anchor.C]))continue;
                var a=link.Inside.Point(points[link.Inner]);var b=link.Outside.Point(points[outer]);
                float needed=Mathf.Max(gap,link.Separation)-Vector3.Dot(b-a,link.Normal);
                if(needed<=.000001f)continue;
                var shift=link.Normal*Mathf.Min(needed,.05f);
                points[outer][anchor.A]+=shift;affected[outer][anchor.A]=true;count++;
                if(anchor.B!=anchor.A){points[outer][anchor.B]+=shift;affected[outer][anchor.B]=true;count++;}
                if(anchor.C!=anchor.A&&anchor.C!=anchor.B){points[outer][anchor.C]+=shift;affected[outer][anchor.C]=true;count++;}
            }
            }
            return count;
        }
    }
}
#endif
