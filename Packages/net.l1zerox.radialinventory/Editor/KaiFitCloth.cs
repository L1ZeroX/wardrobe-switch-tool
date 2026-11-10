#if RISV4_JSON
using System;
using System.Collections.Generic;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiFitCloth
    {
        // Vertices alone cannot detect a convex body piercing a coarse cloth face.
        // Sample the edge midpoints and interior, and project the entire face by
        // a bounded amount. Only originally visible, affected faces participate.
        static readonly Vector3[] Samples=CreateSamples();
        static Vector3[] CreateSamples()
        {
            var result=new List<Vector3>();
            for(int i=0;i<=4;i++)for(int j=0;j<=4-i;j++)result.Add(new Vector3(i/4f,j/4f,1-(i+j)/4f));
            result.Add(new Vector3(1f/3,1f/3,1f/3));return result.ToArray();
        }
        public static int RepairBody(Vector3[] points,Vector3[] normals,int[] triangles,bool[] affected,bool[] allowed,KaiFitSurface body,float gap,float strength=1)
        {
            int count=0;var region=(bool[])affected.Clone();var start=(Vector3[])points.Clone();
            for(int pass=0;pass<6;pass++)
            {
                bool moved=false;
                for(int t=0;t<triangles.Length;t+=3)
                {
                    int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
                    if(!region[a]&&!region[b]&&!region[c])continue;
                    if(allowed!=null&&(!allowed[a]||!allowed[b]||!allowed[c]))continue;
                    foreach(var w in Samples)
                    {
                        var p=points[a]*w.x+points[b]*w.y+points[c]*w.z;
                        var n=(normals[a]*w.x+normals[b]*w.y+normals[c]*w.z).normalized;
                        if(!body.Nearest(p,.01f,out var hit)||Vector3.Dot(n,hit.Normal)<.2f)continue;
                        float depth=gap-Vector3.Dot(p-hit.Point,hit.Normal);
                        // Deeply buried faces may be intentional lining or folds.
                        // Leave those to the user's brush; automatic face finishing
                        // must not pull an entire accessory out through the body.
                        if(depth<=.000001f||depth>gap+.005f)continue;
                        // The closest local skin normal avoids long, almost tangent
                        // exit rays. Barycentric gradients leave unrelated corners
                        // alone instead of repeatedly translating a whole triangle.
                        float projection=w.sqrMagnitude;
                        var shift=hit.Normal*(Mathf.Min(depth/projection,.005f)*strength);
                        var va=start[a]+Vector3.ClampMagnitude(points[a]+shift*w.x-start[a],.002f);
                        var vb=start[b]+Vector3.ClampMagnitude(points[b]+shift*w.y-start[b],.002f);
                        var vc=start[c]+Vector3.ClampMagnitude(points[c]+shift*w.z-start[c],.002f);
                        if((va-points[a]).sqrMagnitude>1e-16f){points[a]=va;affected[a]=true;count++;moved=true;}
                        if((vb-points[b]).sqrMagnitude>1e-16f){points[b]=vb;affected[b]=true;count++;moved=true;}
                        if((vc-points[c]).sqrMagnitude>1e-16f){points[c]=vc;affected[c]=true;count++;moved=true;}
                    }
                }
                if(!moved)break;
            }
            return count;
        }
    }
}
#endif
