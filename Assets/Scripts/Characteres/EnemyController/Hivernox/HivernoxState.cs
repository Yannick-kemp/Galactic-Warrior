namespace Assets.Scripts.Characteres.EnemyContoller
{
    public enum HivernoxState
    {
        Idle,
        DetectWarrior,
        IceAttack,
        FreezeWarrior,
        MoveToWarrior,
        IceBreakerAttack,
        CounterAttack,
        HandSmash,
        Retreat,
        Cooldown,
        Dead,
        // Appended on purpose. The prefab feeds (int)state to the Animator through
        // stateIntParameter ("hivernoxState"), so inserting a value above would shift
        // Retreat/Cooldown/Dead and silently rewire every existing transition.
        Charge,
        ChargeStrike
    }
}
