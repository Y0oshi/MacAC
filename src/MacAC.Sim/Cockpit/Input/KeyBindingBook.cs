using System.Text.Json;
using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

public sealed class KeyBindingBook
{
    private const int LatestSchemaVer = 7;
    private const int QuickSocketIntentVer = 6;
    private const int FightingGripVer = 3;
    private const int PickRightPressVer = 5;

    private readonly List<CockpitBinding> _bindings = [];

    /// <summary>Every binding in insertion order.</summary>
    public IReadOnlyList<CockpitBinding> All => _bindings;

    public void Add(CockpitBinding binding) => _bindings.Add(binding);

    /// <summary>Removes the first structurally equal binding.</summary>
    public bool Remove(CockpitBinding binding) => _bindings.Remove(binding);

    public void Clear() => _bindings.Clear();

    public CockpitBinding? Find(KeyStroke chord, ActivationKind activation)
    {
        foreach (CockpitBinding mapping in _bindings)
        {
            if (mapping.Chord == chord && mapping.Activation == activation)
                return mapping;
        }
        return null;
    }

    public CockpitBinding? Find(KeyStroke chord, ActivationKind activation, InputLayer ambit)
    {
        foreach (CockpitBinding mapping in _bindings)
        {
            if (mapping.Chord == chord && mapping.Activation == activation && mapping.Scope == ambit)
                return mapping;
        }
        return null;
    }

    public IEnumerable<CockpitBinding> ForAct(FeedAct act)
    {
        foreach (CockpitBinding mapping in _bindings)
        {
            if (mapping.Action == act)
                yield return mapping;
        }
    }

    /// <summary>The pre-retail developer layout (WASD, fly, debug keys).</summary>
    public static KeyBindingBook EngineLatestDefaults()
    {
        KeyBindingBook book = new KeyBindingBook();

        book.Bind(Key.W, ModifierBits.None, FeedAct.MovementForward);
        book.Bind(Key.W, ModifierBits.Shift, FeedAct.MovementForward);
        book.Bind(Key.S, ModifierBits.None, FeedAct.MovementBackup);
        book.Bind(Key.S, ModifierBits.Shift, FeedAct.MovementBackup);
        book.Bind(Key.A, ModifierBits.None, FeedAct.MovementTurnLeft);
        book.Bind(Key.A, ModifierBits.Shift, FeedAct.MovementTurnLeft);
        book.Bind(Key.D, ModifierBits.None, FeedAct.MovementTurnRight);
        book.Bind(Key.D, ModifierBits.Shift, FeedAct.MovementTurnRight);
        book.Bind(Key.Z, ModifierBits.None, FeedAct.MovementStrafeLeft);
        book.Bind(Key.Z, ModifierBits.Shift, FeedAct.MovementStrafeLeft);
        book.Bind(Key.X, ModifierBits.None, FeedAct.MovementStrafeRight);
        book.Bind(Key.X, ModifierBits.Shift, FeedAct.MovementStrafeRight);
        book.Bind(Key.ShiftLeft, ModifierBits.Shift, FeedAct.MovementRunLock, ActivationKind.Hold);
        book.Bind(Key.ShiftRight, ModifierBits.Shift, FeedAct.MovementRunLock, ActivationKind.Hold);
        book.Bind(Key.Space, ModifierBits.None, FeedAct.MovementJump);
        book.Bind(Key.Space, ModifierBits.Shift, FeedAct.MovementJump);

        book.Bind(Key.ControlLeft, ModifierBits.Ctrl, FeedAct.EngineFlyDown, ActivationKind.Hold);
        book.Bind(Key.ControlRight, ModifierBits.Ctrl, FeedAct.EngineFlyDown, ActivationKind.Hold);

        book.Bind(Key.F1, ModifierBits.None, FeedAct.EngineToggleDebugPanel);
        book.Bind(Key.F2, ModifierBits.None, FeedAct.EngineToggleCollisionWires);
        book.Bind(Key.F3, ModifierBits.None, FeedAct.EngineDumpNearby);
        book.Bind(Key.F7, ModifierBits.None, FeedAct.EngineCycleTimeOfDay);
        book.Bind(Key.F8, ModifierBits.None, FeedAct.EngineSensitivityDown);
        book.Bind(Key.F9, ModifierBits.None, FeedAct.EngineSensitivityUp);
        book.Bind(Key.M, ModifierBits.Ctrl, FeedAct.EngineToggleAudioMute);
        book.Bind(Key.F10, ModifierBits.None, FeedAct.EngineCycleWeather);
        book.Bind(Key.Tab, ModifierBits.None, FeedAct.EngineTogglePlayerMode);
        book.Bind(Key.Escape, ModifierBits.None, FeedAct.EscapeKey);

        book.Click(MouseButton.Right, FeedAct.EngineRmbOrbitHold, ActivationKind.Hold);
        return book;
    }

    /// <summary>Retail's stock keyboard file, expressed as bindings.</summary>
    public static KeyBindingBook RetailDefaults()
    {
        KeyBindingBook book = new KeyBindingBook();

        book.Bind(Key.W, ModifierBits.None, FeedAct.MovementForward);
        book.Bind(Key.Up, ModifierBits.None, FeedAct.MovementForward);
        book.Bind(Key.X, ModifierBits.None, FeedAct.MovementBackup);
        book.Bind(Key.Down, ModifierBits.None, FeedAct.MovementBackup);
        book.Bind(Key.A, ModifierBits.None, FeedAct.MovementTurnLeft);
        book.Bind(Key.Left, ModifierBits.None, FeedAct.MovementTurnLeft);
        book.Bind(Key.D, ModifierBits.None, FeedAct.MovementTurnRight);
        book.Bind(Key.Right, ModifierBits.None, FeedAct.MovementTurnRight);
        book.Bind(Key.Z, ModifierBits.None, FeedAct.MovementStrafeLeft);
        book.Bind(Key.A, ModifierBits.Alt, FeedAct.MovementStrafeLeft);
        book.Bind(Key.Left, ModifierBits.Alt, FeedAct.MovementStrafeLeft);
        book.Bind(Key.C, ModifierBits.None, FeedAct.MovementStrafeRight);
        book.Bind(Key.D, ModifierBits.Alt, FeedAct.MovementStrafeRight);
        book.Bind(Key.Right, ModifierBits.Alt, FeedAct.MovementStrafeRight);
        book.Bind(Key.ShiftLeft, ModifierBits.None, FeedAct.MovementWalkMode, ActivationKind.Hold);
        book.Bind(Key.Q, ModifierBits.None, FeedAct.MovementRunLock);
        book.Bind(Key.S, ModifierBits.None, FeedAct.MovementStop);
        book.Bind(Key.Y, ModifierBits.None, FeedAct.Ready);
        book.Bind(Key.G, ModifierBits.None, FeedAct.Sitting);
        book.Bind(Key.H, ModifierBits.None, FeedAct.Crouch);
        book.Bind(Key.B, ModifierBits.None, FeedAct.Sleeping);
        book.Bind(Key.Space, ModifierBits.None, FeedAct.MovementJump);

        book.Bind(Key.F, ModifierBits.None, FeedAct.SelectionPlaceInInventory);
        book.Bind(Key.T, ModifierBits.None, FeedAct.SelectionSplitStack);
        book.Bind(Key.P, ModifierBits.None, FeedAct.SelectionPreviousSelection);
        book.Bind(Key.Backspace, ModifierBits.None, FeedAct.SelectionClosestCompassItem);
        book.Bind(Key.Minus, ModifierBits.None, FeedAct.SelectionPreviousCompassItem);
        book.Bind(Key.Equal, ModifierBits.None, FeedAct.SelectionNextCompassItem);
        book.Bind(Key.BackSlash, ModifierBits.None, FeedAct.SelectionClosestItem);
        book.Bind(Key.LeftBracket, ModifierBits.None, FeedAct.SelectionPreviousItem);
        book.Bind(Key.RightBracket, ModifierBits.None, FeedAct.SelectionNextItem);
        book.Bind(Key.Apostrophe, ModifierBits.None, FeedAct.SelectionClosestMonster);
        book.Bind(Key.L, ModifierBits.None, FeedAct.SelectionPreviousMonster);
        book.Bind(Key.Semicolon, ModifierBits.None, FeedAct.SelectionNextMonster);
        book.Bind(Key.Home, ModifierBits.None, FeedAct.SelectionLastAttacker);
        book.Bind(Key.Slash, ModifierBits.None, FeedAct.SelectionClosestPlayer);
        book.Bind(Key.Comma, ModifierBits.None, FeedAct.SelectionPreviousPlayer);
        book.Bind(Key.Period, ModifierBits.None, FeedAct.SelectionNextPlayer);
        book.Bind(Key.N, ModifierBits.None, FeedAct.SelectionPreviousFellow);
        book.Bind(Key.M, ModifierBits.None, FeedAct.SelectionNextFellow);

        book.Bind(Key.E, ModifierBits.None, FeedAct.SelectionExamine);
        book.Bind(Key.KeypadMultiply, ModifierBits.None, FeedAct.CaptureScreenshot);
        book.Bind(Key.F1, ModifierBits.None, FeedAct.ToggleHelp);
        book.Bind(Key.F1, ModifierBits.Shift | ModifierBits.Ctrl, FeedAct.TogglePluginManager);
        book.Bind(Key.F3, ModifierBits.None, FeedAct.ToggleAllegiancePanel);
        book.Bind(Key.F4, ModifierBits.None, FeedAct.ToggleFellowshipPanel);
        book.Bind(Key.F5, ModifierBits.None, FeedAct.ToggleSpellbookPanel);
        book.Bind(Key.F6, ModifierBits.None, FeedAct.ToggleSpellComponentsPanel);
        book.Bind(Key.F8, ModifierBits.None, FeedAct.ToggleAttributesPanel);
        book.Bind(Key.F9, ModifierBits.None, FeedAct.ToggleSkillsPanel);
        book.Bind(Key.F10, ModifierBits.None, FeedAct.ToggleWorldPanel);
        book.Bind(Key.F11, ModifierBits.None, FeedAct.ToggleOptionsPanel);
        book.Bind(Key.F12, ModifierBits.None, FeedAct.ToggleInventoryPanel);
        book.Bind(Key.Number1, ModifierBits.Alt, FeedAct.ToggleFloatingChatWindow1);
        book.Bind(Key.Number2, ModifierBits.Alt, FeedAct.ToggleFloatingChatWindow2);
        book.Bind(Key.Number3, ModifierBits.Alt, FeedAct.ToggleFloatingChatWindow3);
        book.Bind(Key.Number4, ModifierBits.Alt, FeedAct.ToggleFloatingChatWindow4);
        book.Bind(Key.R, ModifierBits.None, FeedAct.UseSelected);
        book.Bind(Key.Escape, ModifierBits.None, FeedAct.EscapeKey);
        book.Bind(Key.Escape, ModifierBits.Shift, FeedAct.LOGOUT);

        for (int idx = 1; idx <= 9; ++idx)
        {
            RetailDefaultsLoop3(idx, book);
        }
        // Alt+1..4 → slots 10..13; Alt+5..9 → slots 14..18
        book.Bind(Key.Number1, ModifierBits.Alt, FeedAct.UseQuickSlot_10);
        book.Bind(Key.Number2, ModifierBits.Alt, FeedAct.UseQuickSlot_11);
        book.Bind(Key.Number3, ModifierBits.Alt, FeedAct.UseQuickSlot_12);
        book.Bind(Key.Number4, ModifierBits.Alt, FeedAct.UseQuickSlot_13);
        for (int idx = 5; idx <= 9; ++idx)
        {
            RetailDefaultsLoop2(idx, book);
        }
        book.Bind(Key.Number0, ModifierBits.None, FeedAct.CreateShortcut);
        book.Bind(Key.Number0, ModifierBits.Ctrl, FeedAct.CreateShortcut);

        book.Bind(Key.Tab, ModifierBits.None, FeedAct.ToggleChatEntry);
        book.Bind(Key.Enter, ModifierBits.None, FeedAct.EnterChatMode);

        book.Bind(Key.GraveAccent, ModifierBits.None, FeedAct.CombatToggleCombat);
        // Melee mode (active when MeleeCombat scope pushed)
        book.Bind(Key.Insert, ModifierBits.None, FeedAct.CombatDecreaseAttackPower, ambit: InputLayer.MeleeCombat);
        book.Bind(Key.PageUp, ModifierBits.None, FeedAct.CombatIncreaseAttackPower, ambit: InputLayer.MeleeCombat);
        book.Bind(Key.Delete, ModifierBits.None, FeedAct.CombatLowAttack, ActivationKind.Hold, InputLayer.MeleeCombat);
        book.Bind(Key.End, ModifierBits.None, FeedAct.CombatMediumAttack, ActivationKind.Hold, InputLayer.MeleeCombat);
        book.Bind(Key.PageDown, ModifierBits.None, FeedAct.CombatHighAttack, ActivationKind.Hold, InputLayer.MeleeCombat);
        book.Bind(Key.Insert, ModifierBits.None, FeedAct.CombatDecreaseMissileAccuracy, ambit: InputLayer.MissileCombat);
        book.Bind(Key.PageUp, ModifierBits.None, FeedAct.CombatIncreaseMissileAccuracy, ambit: InputLayer.MissileCombat);
        book.Bind(Key.Delete, ModifierBits.None, FeedAct.CombatAimLow, ActivationKind.Hold, InputLayer.MissileCombat);
        book.Bind(Key.End, ModifierBits.None, FeedAct.CombatAimMedium, ActivationKind.Hold, InputLayer.MissileCombat);
        book.Bind(Key.PageDown, ModifierBits.None, FeedAct.CombatAimHigh, ActivationKind.Hold, InputLayer.MissileCombat);
        book.Bind(Key.Insert, ModifierBits.None, FeedAct.CombatPrevSpellTab, ambit: InputLayer.MagicCombat);
        book.Bind(Key.PageUp, ModifierBits.None, FeedAct.CombatNextSpellTab, ambit: InputLayer.MagicCombat);
        book.Bind(Key.Delete, ModifierBits.None, FeedAct.CombatPrevSpell, ambit: InputLayer.MagicCombat);
        book.Bind(Key.End, ModifierBits.None, FeedAct.CombatCastCurrentSpell, ambit: InputLayer.MagicCombat);
        book.Bind(Key.PageDown, ModifierBits.None, FeedAct.CombatNextSpell, ambit: InputLayer.MagicCombat);
        book.Bind(Key.Insert, ModifierBits.Ctrl, FeedAct.CombatFirstSpellTab, ambit: InputLayer.MagicCombat);
        book.Bind(Key.PageUp, ModifierBits.Ctrl, FeedAct.CombatLastSpellTab, ambit: InputLayer.MagicCombat);
        book.Bind(Key.Delete, ModifierBits.Ctrl, FeedAct.CombatFirstSpell, ambit: InputLayer.MagicCombat);
        book.Bind(Key.PageDown, ModifierBits.Ctrl, FeedAct.CombatLastSpell, ambit: InputLayer.MagicCombat);
        for (int idx = 1; idx <= 9; ++idx)
        {
            RetailDefaultsLoop(idx, book);
        }

        book.Bind(Key.U, ModifierBits.None, FeedAct.Cry);
        book.Bind(Key.I, ModifierBits.None, FeedAct.Laugh);
        book.Bind(Key.J, ModifierBits.None, FeedAct.Wave);
        book.Bind(Key.O, ModifierBits.None, FeedAct.Cheer);
        book.Bind(Key.K, ModifierBits.None, FeedAct.PointState);

        book.Bind(Key.KeypadDivide, ModifierBits.None, FeedAct.CameraActivateAlternateMode, ActivationKind.Hold);
        book.Bind(Key.F2, ModifierBits.None, FeedAct.CameraActivateAlternateMode, ActivationKind.Hold);
        book.Click(MouseButton.Middle, FeedAct.CameraInstantMouseLook, ActivationKind.Hold);
        book.Bind(Key.Keypad4, ModifierBits.None, FeedAct.CameraRotateLeft, ActivationKind.Hold);
        book.Bind(Key.Keypad6, ModifierBits.None, FeedAct.CameraRotateRight, ActivationKind.Hold);
        book.Bind(Key.Keypad8, ModifierBits.None, FeedAct.CameraRotateUp, ActivationKind.Hold);
        book.Bind(Key.Keypad2, ModifierBits.None, FeedAct.CameraRotateDown, ActivationKind.Hold);
        book.Bind(Key.KeypadSubtract, ModifierBits.None, FeedAct.CameraMoveToward, ActivationKind.Hold);
        book.Bind(Key.KeypadAdd, ModifierBits.None, FeedAct.CameraMoveAway, ActivationKind.Hold);
        book.Bind(Key.Keypad0, ModifierBits.None, FeedAct.CameraViewDefault);
        book.Bind(Key.KeypadDecimal, ModifierBits.None, FeedAct.CameraViewFirstPerson);
        book.Bind(Key.Keypad5, ModifierBits.None, FeedAct.CameraViewLookDown);
        book.Bind(Key.KeypadEnter, ModifierBits.None, FeedAct.CameraViewMapMode);
        book.Bind(Key.Left, ModifierBits.None, FeedAct.CameraAlternateRotateLeft, ActivationKind.Hold, InputLayer.Camera);
        book.Bind(Key.Right, ModifierBits.None, FeedAct.CameraAlternateRotateRight, ActivationKind.Hold, InputLayer.Camera);
        book.Bind(Key.Up, ModifierBits.None, FeedAct.CameraAlternateRotateUp, ActivationKind.Hold, InputLayer.Camera);
        book.Bind(Key.Down, ModifierBits.None, FeedAct.CameraAlternateRotateDown, ActivationKind.Hold, InputLayer.Camera);

        book.Click(MouseButton.Left, FeedAct.SelectLeft);
        book.Click(MouseButton.Right, FeedAct.SelectRight, ActivationKind.Click);
        book.Click(MouseButton.Middle, FeedAct.SelectMid);
        book.Click(MouseButton.Left, FeedAct.SelectDblLeft, ActivationKind.DoubleClick);
        book.Click(MouseButton.Right, FeedAct.SelectDblRight, ActivationKind.DoubleClick);
        book.Click(MouseButton.Middle, FeedAct.SelectDblMid, ActivationKind.DoubleClick);

        book.Bind(Key.Up, ModifierBits.Ctrl, FeedAct.ScrollUp);
        book.Bind(Key.Down, ModifierBits.Ctrl, FeedAct.ScrollDown);

        book.Bind(Key.F1, ModifierBits.Ctrl, FeedAct.EngineToggleDebugPanel);
        book.Bind(Key.F2, ModifierBits.Ctrl, FeedAct.EngineToggleCollisionWires);
        book.Bind(Key.F3, ModifierBits.Ctrl, FeedAct.EngineDumpNearby);
        book.Bind(Key.F7, ModifierBits.Ctrl, FeedAct.EngineCycleTimeOfDay);
        book.Bind(Key.F8, ModifierBits.Ctrl, FeedAct.EngineSensitivityDown);
        book.Bind(Key.F9, ModifierBits.Ctrl, FeedAct.EngineSensitivityUp);
        book.Bind(Key.F10, ModifierBits.Ctrl, FeedAct.EngineCycleWeather);
        book.Bind(Key.M, ModifierBits.Ctrl, FeedAct.EngineToggleAudioMute);

        book.Click(MouseButton.Right, FeedAct.EngineRmbOrbitHold, ActivationKind.Hold);
        return book;
    }

    private static void RetailDefaultsLoop(int idx, KeyBindingBook book)
    {
        Key kdx = (Key)((int)Key.Number0 + idx);
        FeedAct act = (FeedAct)((int)FeedAct.UseSpellSlot_1 + idx - 1);
        book.Bind(kdx, ModifierBits.None, act, ambit: InputLayer.MagicCombat);
    }

    private static void RetailDefaultsLoop2(int idx, KeyBindingBook book)
    {
        Key kdx = (Key)((int)Key.Number0 + idx);
        FeedAct act = (FeedAct)((int)FeedAct.UseQuickSlot_14 + idx - 5);
        book.Bind(kdx, ModifierBits.Alt, act);
    }

    private static void RetailDefaultsLoop3(int idx, KeyBindingBook book)
    {
        Key kdx = (Key)((int)Key.Number0 + idx);
        FeedAct useAct = (FeedAct)((int)FeedAct.UseQuickSlot_1 + idx - 1);
        book.Bind(kdx, ModifierBits.None, useAct);
        book.Bind(kdx, ModifierBits.Ctrl, useAct);
    }

    public static KeyBindingBook PullOrDefault(string trail)
    {
        if (!File.Exists(trail))
            return RetailDefaults();
        try
        {
            using FileStream flow = File.OpenRead(trail);
            JsonElement trunk = JsonDocument.Parse(flow).RootElement;
            // An absent or older version is fine; it only selects migrations
            int ver = trunk.TryGetProperty("version", out JsonElement element) ? element.GetInt32() : 0;

            var defaults = RetailDefaults();
            KeyBindingBook fetched = new KeyBindingBook();
            HashSet<FeedAct> stored = new HashSet<FeedAct>();

            if (trunk.TryGetProperty("actions", out JsonElement acts) && acts.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in acts.EnumerateObject())
                {
                    if (!Enum.TryParse(prop.Name, out FeedAct act) || prop.Value.ValueKind != JsonValueKind.Array)
                        continue;
                    stored.Add(act);
                    foreach (JsonElement listing in prop.Value.EnumerateArray())
                    {
                        if (ScanMapping(listing, act, ver, defaults) is not { } mapping)
                            continue;
                        stored.Add(mapping.Action);
                        fetched.Add(mapping);
                    }
                }
            }

            // Actions the file never mentioned keep their defaults.
            foreach (FeedAct act in Enum.GetValues<FeedAct>())
            {
                if (!stored.Contains(act))
                {
                    foreach (CockpitBinding backup in defaults.ForAct(act))
                        fetched.Add(backup);
                }
            }
            return fetched;
        }
        catch (Exception exc)
        {
            Console.WriteLine($"keybinds: could not load {trail}: {exc.Message} - using retail defaults");
            return RetailDefaults();
        }
    }

    public void StoreToFile(string trail)
    {
        if (Path.GetDirectoryName(trail) is { Length: > 0 } direction)
            Directory.CreateDirectory(direction);

        var acts = new SortedDictionary<string, List<object>>(StringComparer.Ordinal);
        foreach (FeedAct act in CanonActionIdentityTable.Map.Values)
            acts.TryAdd(act.ToString(), []);
        foreach (CockpitBinding mapping in _bindings)
        {
            if (!acts.TryGetValue(mapping.Action.ToString(), out List<object>? roster))
                acts[mapping.Action.ToString()] = roster = [];
            roster.Add(EmitMapping(mapping));
        }

        var trunk = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["version"] = LatestSchemaVer,
            ["actions"] = acts,
        };
        File.WriteAllText(trail, JsonSerializer.Serialize(trunk, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Bind(Key tag, ModifierBits mods, FeedAct act, ActivationKind activation = ActivationKind.Press, InputLayer ambit = InputLayer.Game, byte dev = 0)
    {
        Add(new CockpitBinding(new KeyStroke(tag, mods, dev), act, activation, ambit));
    }

    // A bare mouse-button chord on device 1
    private void Click(MouseButton btn, FeedAct act, ActivationKind activation = ActivationKind.Press)
    {
        Bind(InputRouter.PointerBtnToTag(btn), ModifierBits.None, act, activation, dev: 1);
    }

    // One stored entry: key (required), mod, activation, device, scope; unknown keys are dropped
    private static CockpitBinding? ScanMapping(JsonElement listing, FeedAct action, int ver, KeyBindingBook defaults)
    {
        if (!listing.TryGetProperty("key", out JsonElement tagLabel) || !Enum.TryParse(tagLabel.GetString(), out Key tag))
            return null;

        var mods = DecodeModifiers(listing);
        var activation = ActivationKind.Press;
        if (listing.TryGetProperty("activation", out JsonElement act) && act.ValueKind == JsonValueKind.String && Enum.TryParse(act.GetString(), out ActivationKind decodedAct))
            activation = decodedAct;
        byte device = listing.TryGetProperty("device", out JsonElement dev) && dev.ValueKind == JsonValueKind.Number ? (byte)dev.GetInt32() : (byte)0;

        KeyStroke chord = new KeyStroke(tag, mods, device);
        action = MigrateQuickSocketIntent(ver, action, chord, activation);
        activation = MigrateFightingAssaultActivation(ver, action, activation);
        activation = MigratePickRightActivation(ver, action, activation);

        InputLayer ambit = defaults.ForAct(action).Select(binding => binding.Scope).DefaultIfEmpty(InputLayer.Game).First();
        if (listing.TryGetProperty("scope", out JsonElement element) && element.ValueKind == JsonValueKind.String && Enum.TryParse(element.GetString(), out InputLayer decodedAmbit))
            ambit = decodedAmbit;
        return new CockpitBinding(chord, action, activation, ambit);
    }

    private static SortedDictionary<string, object> EmitMapping(CockpitBinding mapping)
    {
        var listing = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["key"] = mapping.Chord.Key.ToString() };
        if (mapping.Chord.Modifiers != ModifierBits.None)
            listing["mod"] = mapping.Chord.Modifiers.ToString();
        if (mapping.Chord.Device is not 0)
            listing["device"] = (int)mapping.Chord.Device;
        if (mapping.Activation != ActivationKind.Press)
            listing["activation"] = mapping.Activation.ToString();
        if (mapping.Scope != InputLayer.Game)
            listing["scope"] = mapping.Scope.ToString();
        return listing;
    }

    // Before v6, Ctrl+digit on a SelectQuickSlot entry meant "use"; re-file it under the Use action
    private static FeedAct MigrateQuickSocketIntent(int ver, FeedAct act, KeyStroke chord, ActivationKind activation)
    {
        if (ver >= QuickSocketIntentVer || activation != ActivationKind.Press || chord.Device is not 0 || chord.Modifiers != ModifierBits.Ctrl)
            return act;
        int socket = (int)act - (int)FeedAct.SelectQuickSlot_1;
        if ((uint)socket >= 9u)
            return act;
        return chord.Key == (Key)((int)Key.Number1 + socket) ? (FeedAct)((int)FeedAct.UseQuickSlot_1 + socket) : act;
    }

    // Before v3 the attack-height actions were stored as presses; retail holds them
    private static ActivationKind MigrateFightingAssaultActivation(int ver, FeedAct act, ActivationKind activation)
    {
        if (ver >= FightingGripVer || activation != ActivationKind.Press)
            return activation;
        return act is FeedAct.CombatLowAttack or FeedAct.CombatMediumAttack or FeedAct.CombatHighAttack ? ActivationKind.Hold : activation;
    }

    // Before v5 SelectRight fired on press; it is a click now
    private static ActivationKind MigratePickRightActivation(int ver, FeedAct act, ActivationKind activation)
    {
        return ver < PickRightPressVer && act == FeedAct.SelectRight && activation == ActivationKind.Press ? ActivationKind.Click : activation;
    }

    // "Ctrl|Shift", commas and spaces also accepted, case-insensitive
    private static ModifierBits DecodeModifiers(JsonElement listing)
    {
        if (!listing.TryGetProperty("mod", out JsonElement mod) || mod.ValueKind != JsonValueKind.String || mod.GetString() is not { Length: > 0 } spec)
            return ModifierBits.None;
        var outcome = ModifierBits.None;
        foreach (string piece in spec.Split(['|', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse(piece, ignoreCase: true, out ModifierBits bit))
                outcome |= bit;
        }
        return outcome;
    }
}
