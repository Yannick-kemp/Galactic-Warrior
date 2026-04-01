using System;
using UnityEngine;

[Serializable]
public struct KillEvent
{
    public GameObject killer;
    public GameObject victim;
}