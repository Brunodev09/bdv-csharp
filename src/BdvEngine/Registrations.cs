namespace BdvEngine;

public static class Registrations
{
    private static bool _initialized;

    public static void RegisterDefaults()
    {
        if (_initialized) return;
        _initialized = true;

        ComponentManager.RegisterBuilder(new SpriteComponentBuilder());
        ComponentManager.RegisterBuilder(new AnimatedSpriteComponentBuilder());
        ComponentManager.RegisterBuilder(new ColliderComponentBuilder());
        BehaviorManager.RegisterBuilder(new KeyboardMovementBehaviorBuilder());
        BehaviorManager.RegisterBuilder(new RotationBehaviorBuilder());
        BehaviorManager.RegisterBuilder(new PulseBehaviorBuilder());
        BehaviorManager.RegisterBuilder(new RigidBodyBehaviorBuilder());
        BehaviorManager.RegisterBuilder(new RayCastBehaviorBuilder());
        BehaviorManager.RegisterBuilder(new StatefulAnimationBehaviorBuilder());
    }
}
