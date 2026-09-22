namespace MacAC.Cockpit.Input;

public readonly record struct CockpitBinding(KeyStroke Chord, FeedAct Action, ActivationKind Activation = ActivationKind.Press, InputLayer Scope = InputLayer.Game);
