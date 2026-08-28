using System;

namespace Jxqy.Domain.Presentation
{
    public enum JxqyDaoJianGamblePhase
    {
        AwaitingBet,
        Rolling,
        Result,
        Completed,
    }

    public enum JxqyDaoJianGambleResult
    {
        Draw = 0,
        Win = 1,
        Loss = 2,
    }

    /// <summary>
    /// DaoJian 5.4.3 GambleWindow rules. This is deliberately separate from
    /// XinJianXia's original big/small gamble session.
    /// </summary>
    public sealed class JxqyDaoJianGambleSession
    {
        private static readonly int[] Multipliers = { 1, 2, 5, 10 };
        private int _multiplierIndex;

        public JxqyDaoJianGambleSession(
            int playerMoney,
            int npcMoney,
            int gameType,
            string opponentName)
        {
            PlayerMoney = Math.Max(0, playerMoney);
            NpcMoney = Math.Max(0, npcMoney);
            GameType = gameType;
            OpponentName = string.IsNullOrWhiteSpace(opponentName)
                ? "庄家"
                : opponentName.Trim();
        }

        public int PlayerMoney { get; private set; }
        public int NpcMoney { get; private set; }
        public int GameType { get; }
        public string OpponentName { get; }
        public int Bet { get; private set; }
        public int Multiplier => Multipliers[_multiplierIndex];
        public int GambleState { get; private set; }
        public int[] PlayerDice { get; } = new int[3];
        public int[] NpcDice { get; } = new int[3];
        public JxqyDaoJianGamblePhase Phase { get; private set; } =
            JxqyDaoJianGamblePhase.AwaitingBet;
        public JxqyDaoJianGambleResult RoundResult { get; private set; }
        public string PlayerTalk { get; private set; } = string.Empty;
        public string NpcTalk { get; private set; } = string.Empty;
        public string Message { get; private set; } = string.Empty;
        public bool ShouldAutoClose { get; private set; }

        public bool AddBet()
        {
            if (Phase != JxqyDaoJianGamblePhase.AwaitingBet)
                return false;
            int increment = 100 * Multiplier;
            if (PlayerMoney - Bet < increment)
            {
                Message = "银两不足！";
                return false;
            }
            Bet += increment;
            Message = string.Empty;
            return true;
        }

        public bool CycleMultiplier()
        {
            if (Phase != JxqyDaoJianGamblePhase.AwaitingBet)
                return false;
            _multiplierIndex = (_multiplierIndex + 1) % Multipliers.Length;
            return true;
        }

        public bool BeginRoll()
        {
            if (Phase != JxqyDaoJianGamblePhase.AwaitingBet)
                return false;
            if (Bet <= 0)
            {
                Message = "请先下注！";
                return false;
            }
            Message = string.Empty;
            Phase = JxqyDaoJianGamblePhase.Rolling;
            return true;
        }

        public bool ResolveRound(
            int playerDie1,
            int playerDie2,
            int playerDie3,
            int npcDie1,
            int npcDie2,
            int npcDie3)
        {
            if (Phase != JxqyDaoJianGamblePhase.Rolling)
                return false;
            PlayerDice[0] = ValidateDie(playerDie1, nameof(playerDie1));
            PlayerDice[1] = ValidateDie(playerDie2, nameof(playerDie2));
            PlayerDice[2] = ValidateDie(playerDie3, nameof(playerDie3));
            NpcDice[0] = ValidateDie(npcDie1, nameof(npcDie1));
            NpcDice[1] = ValidateDie(npcDie2, nameof(npcDie2));
            NpcDice[2] = ValidateDie(npcDie3, nameof(npcDie3));

            RoundResult = Compare(PlayerDice, NpcDice);
            switch (RoundResult)
            {
                case JxqyDaoJianGambleResult.Win:
                    GambleState = 1;
                    PlayerMoney += Bet;
                    NpcMoney -= Bet;
                    PlayerTalk = Describe(PlayerDice) +
                                 "。在命运的赌桌上，不在乎输赢的人，运气总不会太差！";
                    NpcTalk = Describe(NpcDice) +
                              "。辛辛苦苦二十年，一赌回到解放前！";
                    break;
                case JxqyDaoJianGambleResult.Loss:
                    GambleState = 2;
                    PlayerMoney -= Bet;
                    NpcMoney += Bet;
                    PlayerTalk = Describe(PlayerDice) +
                                 "。一时的输赢并不重要，不要在博弈中迷失自己。";
                    NpcTalk = Describe(NpcDice) +
                              "。没银两了我这有，再来一局怎么样？";
                    break;
                default:
                    PlayerTalk = Describe(PlayerDice) + "。来，决战到天亮！";
                    NpcTalk = Describe(NpcDice) + "。太可惜了，再来一局！";
                    break;
            }

            Bet = 0;
            ShouldAutoClose = GameType == 2;
            if (GameType == 1)
            {
                if (NpcMoney <= 0)
                {
                    GambleState = 1;
                    ShouldAutoClose = true;
                }
                else if (PlayerMoney < 100)
                {
                    GambleState = 2;
                    ShouldAutoClose = true;
                }
            }
            Message = $"你：{FormatDice(PlayerDice)}  {PlayerTalk}\n" +
                      $"{OpponentName}：{FormatDice(NpcDice)}  {NpcTalk}";
            Phase = JxqyDaoJianGamblePhase.Result;
            return true;
        }

        public bool DismissResult()
        {
            if (Phase != JxqyDaoJianGamblePhase.Result || ShouldAutoClose)
                return false;
            Message = string.Empty;
            Phase = JxqyDaoJianGamblePhase.AwaitingBet;
            return true;
        }

        public bool RequestQuit()
        {
            if (Phase != JxqyDaoJianGamblePhase.AwaitingBet)
                return false;
            // GambleWindow.DoHideAnimation uses > 100 here, while its
            // post-round type-1 continuation test uses >= 100.
            if (GameType == 1 && PlayerMoney > 100 && NpcMoney > 0)
            {
                Message = "想走？你身上还有银两呢！";
                return false;
            }
            if (GameType == 0)
                GambleState = 0;
            Phase = JxqyDaoJianGamblePhase.Completed;
            return true;
        }

        public bool CompleteAfterResult()
        {
            if (Phase != JxqyDaoJianGamblePhase.Result || !ShouldAutoClose)
                return false;
            Phase = JxqyDaoJianGamblePhase.Completed;
            return true;
        }

        public static JxqyDaoJianGambleResult Compare(
            int[] player,
            int[] npc)
        {
            ValidateDice(player, nameof(player));
            ValidateDice(npc, nameof(npc));
            int[] left = Sorted(player);
            int[] right = Sorted(npc);
            if (left[0] == right[0] && left[1] == right[1] &&
                left[2] == right[2])
            {
                return JxqyDaoJianGambleResult.Draw;
            }

            int leftRank = Rank(left);
            int rightRank = Rank(right);
            int comparison = leftRank.CompareTo(rightRank);
            if (comparison == 0)
            {
                switch (leftRank)
                {
                    case 3:
                    case 2:
                        comparison = left[2].CompareTo(right[2]);
                        break;
                    case 1:
                        comparison = PairValue(left).CompareTo(
                            PairValue(right));
                        if (comparison == 0)
                        {
                            comparison = SingleValue(left).CompareTo(
                                SingleValue(right));
                        }
                        break;
                    default:
                        comparison = CompareDescending(left, right);
                        break;
                }
            }
            return comparison > 0
                ? JxqyDaoJianGambleResult.Win
                : JxqyDaoJianGambleResult.Loss;
        }

        private static int Rank(int[] sorted)
        {
            if (sorted[0] == sorted[2])
                return 3;
            if (sorted[0] + 1 == sorted[1] &&
                sorted[1] + 1 == sorted[2])
            {
                return 2;
            }
            return sorted[0] == sorted[1] || sorted[1] == sorted[2]
                ? 1
                : 0;
        }

        private static int PairValue(int[] sorted) =>
            sorted[0] == sorted[1] ? sorted[0] : sorted[2];

        private static int SingleValue(int[] sorted) =>
            sorted[0] == sorted[1] ? sorted[2] : sorted[0];

        private static int CompareDescending(int[] left, int[] right)
        {
            for (int index = 2; index >= 0; index--)
            {
                int comparison = left[index].CompareTo(right[index]);
                if (comparison != 0)
                    return comparison;
            }
            return 0;
        }

        private static string Describe(int[] dice)
        {
            switch (Rank(Sorted(dice)))
            {
                case 3: return "鸿运当头，豹子";
                case 2: return "顺水顺风，顺子";
                case 1: return "好运连连，对子";
                default: return "普通点数";
            }
        }

        private static string FormatDice(int[] dice) =>
            $"{dice[0]}、{dice[1]}、{dice[2]}";

        private static int[] Sorted(int[] values)
        {
            var result = (int[])values.Clone();
            Array.Sort(result);
            return result;
        }

        private static void ValidateDice(int[] dice, string parameterName)
        {
            if (dice == null || dice.Length != 3)
                throw new ArgumentException("Exactly three dice are required.", parameterName);
            for (int index = 0; index < dice.Length; index++)
                ValidateDie(dice[index], parameterName);
        }

        private static int ValidateDie(int value, string parameterName)
        {
            if (value < 1 || value > 6)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }
}
