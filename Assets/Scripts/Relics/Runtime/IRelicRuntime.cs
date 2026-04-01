// Assets/Scripts/Relics/Runtime/IRelicRuntime.cs
using Assets.Scripts.Relics.Events;

namespace Assets.Scripts.Relics.Runtime
{
    public interface IRelicRuntime
    {
        void OnEquip(RelicContext ctx);
        void OnUnequip();
        void OnHit(HitEvent e);
        void OnKill(KillEvent e);
    }
}
