using System.Collections.Generic;

namespace KinectIrWandGestures
{
    /// <summary>
    /// Starter templates for quick testing. These are intentionally simple, unistroke approximations.
    /// You will get best results by recording your own versions later.
    /// </summary>
    public static class DefaultSpellTemplates
    {
        public static List<SpellTemplate> Build()
        {
            var list = new List<SpellTemplate>();

            // Helpers (no tuples)
            List<XY> P(params double[] xy)
            {
                var pts = new List<XY>();
                for (int i = 0; i + 1 < xy.Length; i += 2)
                    pts.Add(new XY { X = xy[i], Y = xy[i + 1] });
                return pts;
            }

            // ---- Basic primitives ----
            var LINE_RIGHT = P(0, 50, 100, 50);
            var LINE_LEFT = P(100, 50, 0, 50);
            var LINE_DOWN = P(50, 0, 50, 100);
            var LINE_UP = P(50, 100, 50, 0);

            var V_SHAPE = P(10, 20, 50, 90, 90, 20);
            var M_SHAPE = P(10, 85, 30, 20, 50, 85, 70, 20, 90, 85);
            var N_SHAPE = P(15, 85, 15, 20, 85, 85, 85, 20);
            var Z_SHAPE = P(15, 20, 85, 20, 15, 85, 85, 85);

            var TRIANGLE = P(50, 10, 90, 85, 10, 85, 50, 10);

            var S_SHAPE = P(80, 20, 40, 10, 20, 30, 60, 50, 80, 70, 60, 90, 20, 80);

            var CIRCLE = P(
                50, 10, 70, 15, 85, 30, 90, 50, 85, 70, 70, 85, 50, 90, 30, 85, 15, 70, 10, 50, 15, 30, 30, 15, 50, 10
            );

            var SPIRAL = P(
                55, 15, 70, 20, 80, 35, 80, 55, 70, 70, 55, 75, 40, 70, 35, 55, 40, 40, 52, 35, 60, 42, 58, 55, 50, 60
            );

            var HOOK_UP = P(30, 85, 30, 35, 50, 15, 70, 25);
            var HOOK_DOWN = P(30, 15, 30, 65, 50, 85, 70, 75);

            var L_SHAPE = P(25, 15, 25, 85, 85, 85);

            var ARROW_RIGHT = P(10, 50, 80, 50, 65, 35, 80, 50, 65, 65);
            var ARROW_LEFT = P(90, 50, 20, 50, 35, 35, 20, 50, 35, 65);

            var LOOP_LEFT = P(70, 20, 40, 20, 20, 40, 20, 60, 40, 80, 70, 80, 85, 65, 75, 55, 55, 55);

            var LOOP_RIGHT = P(30, 20, 60, 20, 80, 40, 80, 60, 60, 80, 30, 80, 15, 65, 25, 55, 45, 55);

            var CROSS = P(50, 10, 50, 90, 50, 50, 10, 50, 90, 50); // unistroke cross via center

            // ---- Spell set from the image ----
            list.Add(new SpellTemplate { Name = "Accio", Points = LOOP_LEFT });

            list.Add(new SpellTemplate { Name = "Aguamenti", Points = P(10, 30, 35, 20, 55, 25, 70, 40, 75, 60, 65, 75, 45, 80, 25, 70) });

            list.Add(new SpellTemplate { Name = "Alohomora", Points = ARROW_LEFT });

            list.Add(new SpellTemplate { Name = "Aparcium", Points = P(10, 50, 25, 35, 45, 30, 65, 40, 80, 55, 70, 70, 50, 75, 30, 65) });

            list.Add(new SpellTemplate { Name = "Arresto Momentum", Points = M_SHAPE });

            list.Add(new SpellTemplate { Name = "Ascendio", Points = HOOK_UP });

            list.Add(new SpellTemplate { Name = "Avis", Points = P(10, 55, 30, 40, 50, 45, 70, 40, 90, 55) });

            list.Add(new SpellTemplate { Name = "Confringo", Points = Z_SHAPE });

            list.Add(new SpellTemplate { Name = "Confundus", Points = P(15, 25, 85, 25, 55, 55, 85, 85) });

            list.Add(new SpellTemplate { Name = "Defodio", Points = L_SHAPE });

            list.Add(new SpellTemplate { Name = "Descendo", Points = HOOK_DOWN });

            list.Add(new SpellTemplate { Name = "Diffindo", Points = N_SHAPE });

            list.Add(new SpellTemplate { Name = "Duro", Points = P(20, 15, 20, 85, 55, 85, 80, 65, 55, 50, 20, 50) });

            list.Add(new SpellTemplate { Name = "Engorgio", Points = V_SHAPE });

            list.Add(new SpellTemplate { Name = "Episkey", Points = CIRCLE });

            list.Add(new SpellTemplate { Name = "Expecto Patronum", Points = SPIRAL });

            list.Add(new SpellTemplate { Name = "Expelliarmus", Points = LINE_RIGHT });

            list.Add(new SpellTemplate { Name = "Finite Incantatem", Points = P(20, 20, 80, 20, 80, 50, 50, 50, 50, 85) });

            list.Add(new SpellTemplate { Name = "Herbivicus", Points = P(50, 85, 50, 20, 70, 20, 70, 55) });

            list.Add(new SpellTemplate { Name = "Impedimenta", Points = LINE_LEFT });

            list.Add(new SpellTemplate { Name = "Incendio", Points = P(20, 85, 50, 20, 80, 85) }); // caret-like flame

            list.Add(new SpellTemplate { Name = "Locomotor", Points = CROSS });

            list.Add(new SpellTemplate { Name = "Lumos", Points = P(50, 20, 35, 35, 50, 50, 65, 35, 50, 20) });

            list.Add(new SpellTemplate { Name = "Meteolojinx", Points = P(25, 70, 35, 40, 55, 30, 75, 40, 85, 70) });

            list.Add(new SpellTemplate { Name = "Mimblewimble", Points = P(20, 65, 40, 35, 60, 35, 80, 65, 60, 75, 40, 75) });

            list.Add(new SpellTemplate { Name = "Oppugno", Points = P(70, 15, 30, 85, 70, 85) });

            list.Add(new SpellTemplate { Name = "Orchideous", Points = P(25, 25, 75, 75, 25, 75, 75, 25) });

            list.Add(new SpellTemplate { Name = "Petrificus Totalus", Points = ARROW_RIGHT });

            list.Add(new SpellTemplate { Name = "Protego", Points = V_SHAPE });

            list.Add(new SpellTemplate { Name = "Reducio", Points = P(15, 35, 50, 20, 85, 35, 65, 55, 50, 40, 35, 55) });

            list.Add(new SpellTemplate { Name = "Reparo", Points = P(20, 85, 20, 20, 70, 20, 70, 50, 20, 50, 70, 85) });

            list.Add(new SpellTemplate { Name = "Revelio", Points = P(50, 20, 50, 80, 65, 80, 80, 65, 65, 50) });

            list.Add(new SpellTemplate { Name = "Scourgify", Points = S_SHAPE });

            list.Add(new SpellTemplate { Name = "Serpensortia", Points = P(20, 20, 60, 20, 80, 35, 60, 50, 40, 65, 60, 80) });

            list.Add(new SpellTemplate { Name = "Silencio", Points = P(80, 20, 20, 20, 20, 80, 80, 80, 80, 50) });

            list.Add(new SpellTemplate { Name = "Specialis Revelio", Points = SPIRAL });

            list.Add(new SpellTemplate { Name = "Stupefy", Points = LINE_DOWN });

            list.Add(new SpellTemplate { Name = "Tarantallegra", Points = P(20, 60, 35, 40, 50, 60, 65, 40, 80, 60) });

            list.Add(new SpellTemplate { Name = "Wingardium Leviosa", Points = P(20, 60, 35, 40, 50, 60, 65, 40, 80, 60, 90, 75) });

            return list;
        }
    }
}
