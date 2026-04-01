// Assets/Scripts/Relics/Runtime/RelicRuntimeBase.cs
using Assets.Scripts.Relics.Events;

namespace Assets.Scripts.Relics.Runtime
{
    public abstract class RelicRuntimeBase : IRelicRuntime
    {
        protected RelicContext Ctx;

        public virtual void OnEquip(RelicContext ctx) => Ctx = ctx;
        public virtual void OnUnequip() { }
        public virtual void OnHit(HitEvent e) { }
        public virtual void OnKill(KillEvent e) { }
    }
}
