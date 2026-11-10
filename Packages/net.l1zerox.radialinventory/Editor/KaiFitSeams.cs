#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    // UV/material/normal splits are separate indices, but not separate cloth.
    // Only join identical bind positions, skin weights and all shape-frame deltas.
    // The mesh topology, UVs and normals themselves are never welded or replaced.
    public sealed class KaiFitSeams
    {
        readonly int[][] groups;
        readonly int[][] neighbours;
        public int GroupCount=>groups.Length;
        public KaiFitSeams(Mesh mesh)
        {
            var vertices=mesh.vertices;var weights=mesh.boneWeights;
            var candidates=Enumerable.Range(0,vertices.Length).GroupBy(i=>vertices[i])
                .SelectMany(g=>weights.Length==vertices.Length?g.GroupBy(i=>weights[i]).Select(w=>w.ToArray()):new[]{g.ToArray()})
                .Where(g=>g.Length>1).ToList();
            var delta=new Vector3[vertices.Length];
            for(int shape=0;shape<mesh.blendShapeCount;shape++)for(int frame=0;frame<mesh.GetBlendShapeFrameCount(shape);frame++)
            {
                mesh.GetBlendShapeFrameVertices(shape,frame,delta,null,null);
                candidates=candidates.SelectMany(g=>g.GroupBy(i=>delta[i]).Select(v=>v.ToArray())).Where(g=>g.Length>1).ToList();
            }
            groups=candidates.ToArray();
            var representative=Enumerable.Range(0,vertices.Length).ToArray();
            foreach(var g in groups)foreach(int i in g)representative[i]=g[0];
            var adjacent=Enumerable.Range(0,vertices.Length).Select(i=>new HashSet<int>()).ToArray();var triangles=mesh.triangles;
            for(int t=0;t<triangles.Length;t+=3)for(int e=0;e<3;e++)
            {int a=representative[triangles[t+e]],b=representative[triangles[t+(e+1)%3]];if(a!=b){adjacent[a].Add(b);adjacent[b].Add(a);}}
            neighbours=Enumerable.Range(0,vertices.Length).Select(i=>adjacent[representative[i]].ToArray()).ToArray();
        }
        public int SmoothOutside(Vector3[] offsets,Vector3[] points,Matrix4x4[] inverseSkin,bool[] affected,KaiFitSurface body,float gap)
        {
            var previous=(Vector3[])offsets.Clone();int count=0;
            for(int i=0;i<offsets.Length;i++)
            {
                if(!affected[i]||neighbours[i].Length==0||!body.Nearest(points[i],.03f,out var hit))continue;
                if(Vector3.Dot(points[i]-hit.Point,hit.Normal)<=gap+.001f)continue;
                var mean=Vector3.zero;foreach(int n in neighbours[i])mean+=previous[n];mean/=neighbours[i].Length;
                var worldDelta=inverseSkin[i].inverse.MultiplyVector((mean-previous[i])*.5f);
                if(worldDelta.sqrMagnitude<.000001f)continue;
                var candidate=points[i]+Vector3.ClampMagnitude(worldDelta,.002f);
                if(body.Nearest(candidate,.03f,out var surface))
                {float depth=gap-Vector3.Dot(candidate-surface.Point,surface.Normal);if(depth>0)candidate+=surface.Normal*depth;}
                offsets[i]+=inverseSkin[i].MultiplyVector(candidate-points[i]);count++;
            }
            return count;
        }
        public int Synchronize(Vector3[] offsets,bool[] affected=null,bool restore=false)
        {
            int count=0;
            foreach(var group in groups)
            {
                if(affected!=null&&!group.Any(i=>affected[i]))continue;
                int best=group[0];
                foreach(int i in group)if(restore?offsets[i].sqrMagnitude<offsets[best].sqrMagnitude:offsets[i].sqrMagnitude>offsets[best].sqrMagnitude)best=i;
                var value=offsets[best];
                foreach(int i in group)
                {
                    if((offsets[i]-value).sqrMagnitude>1e-16f){offsets[i]=value;count++;}
                    if(affected!=null)affected[i]=true;
                }
            }
            return count;
        }
    }
}
#endif
