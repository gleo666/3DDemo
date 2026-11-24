using System;
using UnityEngine;

namespace DialogueSystem
{
    [Serializable]
    public class EventGroup
    {
        public string   EventName;
        public string   PromptText;
        public string[] EventText;
        public EventEnd End;
    }

    [Serializable]
    public class EventEnd
    {
        public string   Type;
        public string[] Set;
        public bool     DestroySelf;
        public string   NextEvent;
        public string   Condition;
        public string   TrueEvent;
        public string   FalseEvent;
    }

    [Serializable]
    public class EventDatabase
    {
        public EventGroup[] Events;
    }
}