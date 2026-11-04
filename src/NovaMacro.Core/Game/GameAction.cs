using NovaMacro.Core.Input;

namespace NovaMacro.Core.Game;

/// <summary>GTA V controls the macros drive. Each resolves to the user's actual binding.</summary>
public enum GameAction
{
    Pause,
    SocialClubHome,
    InteractionMenu,
    MenuUp,
    MenuDown,
    MenuLeft,
    MenuRight,
    MenuSelect,
    MenuBack,
    Phone,
    PhoneScrollForward,
    PhoneScrollBackward,
    MoveForward,
    MoveBackward,
    MoveLeft,
    MoveRight,
    Jump,
    Sprint,
    Cover,
    Attack,
    Aim,
    WeaponWheel,
    WeaponUnarmed,
    WeaponMelee,
    WeaponThrowable,
    WeaponPistol,
    WeaponAssaultRifle,
    WeaponSniper,
    WeaponHeavy,
    WeaponShotgun,
    WeaponSmg,
    EatSnack,
    UseArmor,
    NextCamera,
    HudSpecial,
    CharacterWheel,
    SelectMichael,
    SelectFranklin,
    SelectTrevor,
    PlaneThrottleUp,
    PlaneThrottleDown,
    PlaneYawLeft,
    PlaneYawRight,
    PlanePitchUp,
    PlanePitchDown,
    PlaneRollLeft,
    PlaneRollRight,
}

public sealed record GameActionInfo(GameAction Action, string DisplayName, string GtaInput, int DefaultCode);

public static class GameActions
{
    public static IReadOnlyList<GameActionInfo> All { get; } = new GameActionInfo[]
    {
        new(GameAction.Pause, "暂停菜单", "INPUT_FRONTEND_PAUSE", InputCodes.Escape),
        new(GameAction.SocialClubHome, "HOME菜单", "INPUT_FRONTEND_SOCIAL_CLUB_SECONDARY", InputCodes.Home),
        new(GameAction.InteractionMenu, "M菜单呼出", "INPUT_INTERACTION_MENU", InputCodes.Letter('M')),
        new(GameAction.MenuUp, "M菜单手机上选", "INPUT_CELLPHONE_UP", InputCodes.Up),
        new(GameAction.MenuDown, "M菜单手机下选", "INPUT_CELLPHONE_DOWN", InputCodes.Down),
        new(GameAction.MenuLeft, "M菜单手机左选", "INPUT_CELLPHONE_LEFT", InputCodes.Left),
        new(GameAction.MenuRight, "M菜单手机右选", "INPUT_CELLPHONE_RIGHT", InputCodes.Right),
        new(GameAction.MenuSelect, "M菜单手机选择", "INPUT_CELLPHONE_SELECT", InputCodes.Enter),
        new(GameAction.MenuBack, "M菜单手机返回", "INPUT_CELLPHONE_CANCEL", InputCodes.Backspace),
        new(GameAction.Phone, "手机呼出", "INPUT_PHONE", InputCodes.Up),
        new(GameAction.PhoneScrollForward, "手机前滚", "INPUT_CELLPHONE_SCROLL_FORWARD", InputCodes.WheelDown),
        new(GameAction.PhoneScrollBackward, "手机后滚", "INPUT_CELLPHONE_SCROLL_BACKWARD", InputCodes.WheelUp),
        new(GameAction.MoveForward, "前进", "INPUT_MOVE_UP_ONLY", InputCodes.Letter('W')),
        new(GameAction.MoveBackward, "后退", "INPUT_MOVE_DOWN_ONLY", InputCodes.Letter('S')),
        new(GameAction.MoveLeft, "左移动", "INPUT_MOVE_LEFT_ONLY", InputCodes.Letter('A')),
        new(GameAction.MoveRight, "右移动", "INPUT_MOVE_RIGHT_ONLY", InputCodes.Letter('D')),
        new(GameAction.Jump, "跳跃", "INPUT_JUMP", InputCodes.Space),
        new(GameAction.Sprint, "奔跑", "INPUT_SPRINT", InputCodes.LShift),
        new(GameAction.Cover, "找掩护", "INPUT_COVER", InputCodes.Letter('Q')),
        new(GameAction.Attack, "武器射击", "INPUT_ATTACK", InputCodes.LeftButton),
        new(GameAction.Aim, "武器瞄准", "INPUT_AIM", InputCodes.RightButton),
        new(GameAction.WeaponWheel, "武器选单", "INPUT_SELECT_WEAPON", InputCodes.Tab),
        new(GameAction.WeaponUnarmed, "武器选择_徒手", "INPUT_SELECT_WEAPON_UNARMED", InputCodes.Digit(1)),
        new(GameAction.WeaponMelee, "武器选择_近战", "INPUT_SELECT_WEAPON_MELEE", InputCodes.Digit(2)),
        new(GameAction.WeaponShotgun, "武器选择_霰弹枪", "INPUT_SELECT_WEAPON_SHOTGUN", InputCodes.Digit(3)),
        new(GameAction.WeaponHeavy, "武器选择_重型武器", "INPUT_SELECT_WEAPON_HEAVY", InputCodes.Digit(4)),
        new(GameAction.WeaponThrowable, "武器选择_投掷", "INPUT_SELECT_WEAPON_SPECIAL", InputCodes.Digit(5)),
        new(GameAction.WeaponPistol, "武器选择_手枪", "INPUT_SELECT_WEAPON_HANDGUN", InputCodes.Digit(6)),
        new(GameAction.WeaponSmg, "武器选择_冲锋枪", "INPUT_SELECT_WEAPON_SMG", InputCodes.Digit(7)),
        new(GameAction.WeaponAssaultRifle, "武器选择_突击步枪", "INPUT_SELECT_WEAPON_AUTO_RIFLE", InputCodes.Digit(8)),
        new(GameAction.WeaponSniper, "武器选择_狙击枪", "INPUT_SELECT_WEAPON_SNIPER", InputCodes.Digit(9)),
        new(GameAction.EatSnack, "轮盘吃零食", "INPUT_EAT_SNACK", InputCodes.Letter('C')),
        new(GameAction.UseArmor, "轮盘穿护甲", "INPUT_USE_ARMOR", InputCodes.Letter('X')),
        new(GameAction.NextCamera, "切换人称", "INPUT_NEXT_CAMERA", InputCodes.Letter('V')),
        new(GameAction.HudSpecial, "HUD特殊", "INPUT_HUD_SPECIAL", InputCodes.Letter('Z')),
        new(GameAction.CharacterWheel, "角色选择器", "INPUT_CHARACTER_WHEEL", InputCodes.LAlt),
        new(GameAction.SelectMichael, "选择麦克", "INPUT_SELECT_CHARACTER_MICHAEL", InputCodes.Function(5)),
        new(GameAction.SelectFranklin, "选择富兰克林", "INPUT_SELECT_CHARACTER_FRANKLIN", InputCodes.Function(6)),
        new(GameAction.SelectTrevor, "选择崔佛", "INPUT_SELECT_CHARACTER_TREVOR", InputCodes.Function(7)),
        new(GameAction.PlaneThrottleUp, "飞机_加速", "INPUT_VEH_FLY_THROTTLE_UP", InputCodes.Letter('W')),
        new(GameAction.PlaneThrottleDown, "飞机_减速", "INPUT_VEH_FLY_THROTTLE_DOWN", InputCodes.Letter('S')),
        new(GameAction.PlaneYawLeft, "飞机_左偏航", "INPUT_VEH_FLY_YAW_LEFT", InputCodes.Letter('A')),
        new(GameAction.PlaneYawRight, "飞机_右偏航", "INPUT_VEH_FLY_YAW_RIGHT", InputCodes.Letter('D')),
        new(GameAction.PlanePitchUp, "飞机_爬升", "INPUT_VEH_FLY_PITCH_UP_ONLY", InputCodes.Numpad0 + 5),
        new(GameAction.PlanePitchDown, "飞机_俯冲", "INPUT_VEH_FLY_PITCH_DOWN_ONLY", InputCodes.Numpad0 + 8),
        new(GameAction.PlaneRollLeft, "飞机_左翻滚", "INPUT_VEH_FLY_ROLL_LEFT_ONLY", InputCodes.Numpad0 + 4),
        new(GameAction.PlaneRollRight, "飞机_右翻滚", "INPUT_VEH_FLY_ROLL_RIGHT_ONLY", InputCodes.Numpad0 + 6),
    };

    public static GameActionInfo Info(GameAction action) => All.First(a => a.Action == action);

    public static bool TryParse(string text, out GameAction action)
    {
        foreach (var info in All)
        {
            if (string.Equals(info.DisplayName, text, StringComparison.Ordinal) ||
                string.Equals(info.Action.ToString(), text, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.GtaInput, text, StringComparison.OrdinalIgnoreCase))
            {
                action = info.Action;
                return true;
            }
        }

        action = default;
        return false;
    }
}
