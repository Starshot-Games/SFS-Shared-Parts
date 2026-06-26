using UnityEngine;
using SFS.Variables;
using Sirenix.OdinInspector;

namespace SFS.Parts.Modules
{
    [HideMonoScript]
    public class SimplePipe : PipeData, I_InitializePartModule
    {
        public Composed_Float width_a;
        public Composed_Float width_b;
        public Composed_Float height_a;
        public Composed_Float height_b;

        public float offsetX;
        
        [BoxGroup("edit", false), HorizontalGroup("edit/a")] public bool view = true;

        int I_InitializePartModule.Priority => 10;
        void I_InitializePartModule.Initialize()
        {
            width_a.OnChange += Output;
            width_b.OnChange += Output;
            height_a.OnChange += Output;
            height_b.OnChange += Output;
        }

        public override void Output()
        {
            Pipe pipe = new Pipe();

            pipe.AddPoint(new Vector2(offsetX, height_a.Value), Vector2.right * width_a.Value);
            pipe.AddPoint(new Vector2(offsetX, height_b.Value), Vector2.right * width_b.Value);

            SetData(pipe);
        }
        
        // debug draw gizmos
        void OnDrawGizmos()
        {
            if (!view)
                return;
            
            Vector3 a = new Vector3(offsetX - width_a.Value / 2, height_a.Value, 0);
            Vector3 b = new Vector3(offsetX + width_a.Value / 2, height_a.Value, 0);
            Vector3 c = new Vector3(offsetX - width_b.Value / 2, height_b.Value, 0);
            Vector3 d = new Vector3(offsetX + width_b.Value / 2, height_b.Value, 0);


            // local to world
            a = transform.TransformPoint(a);
            b = transform.TransformPoint(b);
            c = transform.TransformPoint(c);
            d = transform.TransformPoint(d);
            
            Gizmos.color = Color.green;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, d);
            Gizmos.DrawLine(d, c);
            Gizmos.DrawLine(c, a);
            
            
            // cut preview
            if (cut < 0 || cut > 0)
            {
                float lerp = (cut < 0 ? 2 + cut : cut) / 2;
                
                Vector3 cut_Bottom = Vector3.Lerp(a, b, lerp);
                Vector3 cut_Top = Vector3.Lerp(c, d, lerp);
                
                Gizmos.color = Color.red;
                Gizmos.DrawLine(cut_Bottom, cut_Top);
            }
        }
    }
}