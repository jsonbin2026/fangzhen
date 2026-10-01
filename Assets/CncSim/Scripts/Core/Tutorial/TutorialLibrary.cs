using System;
using System.Collections.Generic;
using System.Text;
using CncSim.Core.Localization;

namespace CncSim.Core.Tutorial
{
    public enum TutorialStepType
    {
        Info,
        Highlight,
        TryIt
    }

    /// <summary>教程中的一步。</summary>
    public class TutorialStep
    {
        public TutorialStepType Type = TutorialStepType.Info;
        /// <summary>标题词条键。</summary>
        public string TitleKey;
        /// <summary>正文词条键。</summary>
        public string BodyKey;
        /// <summary>高亮的 UI 元素 id（由界面层约定）。</summary>
        public string TargetElementId;

        public string Title => Loc.Get(TitleKey);
        public string Body => Loc.Get(BodyKey);
    }

    /// <summary>一个教程。</summary>
    public class Tutorial
    {
        public string Id;
        public string TitleKey;
        public string DescriptionKey;
        public readonly List<TutorialStep> Steps = new List<TutorialStep>();

        public string Title => Loc.Get(TitleKey);
        public string Description => Loc.Get(DescriptionKey);
    }

    /// <summary>
    /// 内置教程 / 帮助（功能 94）。提供分步引导与 G/M 速查入口。
    /// </summary>
    public static class TutorialLibrary
    {
        public static List<Tutorial> All()
        {
            return new List<Tutorial>
            {
                new Tutorial
                {
                    Id = "getting-started",
                    TitleKey = "tutorial.getting_started.title",
                    DescriptionKey = "tutorial.getting_started.desc",
                    Steps =
                    {
                        Step(TutorialStepType.Info, "tutorial.gs.s1.title", "tutorial.gs.s1.body"),
                        Step(TutorialStepType.Highlight, "tutorial.gs.s2.title", "tutorial.gs.s2.body", "editor"),
                        Step(TutorialStepType.TryIt, "tutorial.gs.s3.title", "tutorial.gs.s3.body", "open-button"),
                        Step(TutorialStepType.TryIt, "tutorial.gs.s4.title", "tutorial.gs.s4.body", "simulate-button"),
                        Step(TutorialStepType.Info, "tutorial.gs.s5.title", "tutorial.gs.s5.body", "viewport"),
                    }
                },
                new Tutorial
                {
                    Id = "simulation",
                    TitleKey = "tutorial.simulation.title",
                    DescriptionKey = "tutorial.simulation.desc",
                    Steps =
                    {
                        Step(TutorialStepType.Info, "tutorial.sim.s1.title", "tutorial.sim.s1.body", "tool-panel"),
                        Step(TutorialStepType.TryIt, "tutorial.sim.s2.title", "tutorial.sim.s2.body", "stock-panel"),
                        Step(TutorialStepType.Info, "tutorial.sim.s3.title", "tutorial.sim.s3.body", "transport"),
                        Step(TutorialStepType.TryIt, "tutorial.sim.s4.title", "tutorial.sim.s4.body", "speed-slider"),
                        Step(TutorialStepType.Info, "tutorial.sim.s5.title", "tutorial.sim.s5.body", "timeline"),
                    }
                },
                new Tutorial
                {
                    Id = "five-axis",
                    TitleKey = "tutorial.fiveaxis.title",
                    DescriptionKey = "tutorial.fiveaxis.desc",
                    Steps =
                    {
                        Step(TutorialStepType.Info, "tutorial.5x.s1.title", "tutorial.5x.s1.body", "machine-selector"),
                        Step(TutorialStepType.Info, "tutorial.5x.s2.title", "tutorial.5x.s2.body"),
                        Step(TutorialStepType.TryIt, "tutorial.5x.s3.title", "tutorial.5x.s3.body", "kinematics"),
                    }
                }
            };
        }

        private static TutorialStep Step(TutorialStepType type, string title, string body, string target = null) =>
            new TutorialStep { Type = type, TitleKey = title, BodyKey = body, TargetElementId = target };

        /// <summary>运行一个教程的会话状态。</summary>
        public class Session
        {
            private readonly Tutorial _tutorial;
            public int Index { get; private set; }
            public Tutorial Current => _tutorial;

            public Session(Tutorial tutorial)
            {
                _tutorial = tutorial ?? throw new ArgumentNullException(nameof(tutorial));
            }

            public TutorialStep CurrentStep =>
                Index >= 0 && Index < _tutorial.Steps.Count ? _tutorial.Steps[Index] : null;

            public bool IsFinished => Index >= _tutorial.Steps.Count;
            public float Progress => _tutorial.Steps.Count == 0 ? 1f : (float)Index / _tutorial.Steps.Count;

            public TutorialStep Next()
            {
                if (Index < _tutorial.Steps.Count) Index++;
                return CurrentStep;
            }

            public TutorialStep Previous()
            {
                if (Index > 0) Index--;
                return CurrentStep;
            }

            public void Reset() => Index = 0;
        }
    }
}
