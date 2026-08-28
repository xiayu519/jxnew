using System;

namespace Jxqy.Domain.Presentation
{
    public enum JxqyGambleOpponent
    {
        LuWencai = 0,
        CasinoOwner = 1,
    }

    public enum JxqyGambleChoice
    {
        Big = 0,
        Small = 1,
    }

    public enum JxqyGamblePhase
    {
        AwaitingBet,
        Rolling,
        Opening,
        ResultMessage,
        NoticeMessage,
        SettlementMessage,
        Completed,
    }

    /// <summary>
    /// Original three-dice gambling rules recovered from the shipped EXE.
    /// </summary>
    public sealed class JxqyGambleSession
    {
        private static readonly string[] ChineseNumbers =
        {
            "零", "一", "二", "三", "四", "五", "六", "七", "八", "九",
            "十", "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八",
        };

        public JxqyGambleSession(int initialMoney, int opponentType)
        {
            if (initialMoney <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialMoney));
            InitialMoney = initialMoney;
            AvailableMoney = initialMoney;
            OpponentMoney = initialMoney;
            Opponent = opponentType == (int)JxqyGambleOpponent.CasinoOwner
                ? JxqyGambleOpponent.CasinoOwner
                : JxqyGambleOpponent.LuWencai;
        }

        public int InitialMoney { get; }
        public int AvailableMoney { get; private set; }
        public int PlayerMoney => AvailableMoney + Stake;
        public int OpponentMoney { get; private set; }
        public int Stake { get; private set; }
        public JxqyGambleOpponent Opponent { get; }
        public JxqyGambleChoice Choice { get; private set; }
        public bool HasChoice { get; private set; } = true;
        public JxqyGamblePhase Phase { get; private set; } =
            JxqyGamblePhase.AwaitingBet;
        public int Die1 { get; private set; }
        public int Die2 { get; private set; }
        public int Die3 { get; private set; }
        public bool HasRolledDice { get; private set; }
        public int DiceTotal => Die1 + Die2 + Die3;
        public bool DiceAreSmall => DiceTotal < 11;
        public string Message { get; private set; } = string.Empty;
        public int NetMoneyChange { get; private set; }
        public bool ScriptResult => NetMoneyChange >= 0;

        public bool IncreaseStake()
        {
            if (Phase != JxqyGamblePhase.AwaitingBet ||
                AvailableMoney <= 0 || Stake >= OpponentMoney)
                return false;
            AvailableMoney--;
            Stake++;
            return true;
        }

        public bool DecreaseStake()
        {
            if (Phase != JxqyGamblePhase.AwaitingBet || Stake <= 1)
                return false;
            Stake--;
            AvailableMoney++;
            return true;
        }

        public bool Select(JxqyGambleChoice choice)
        {
            if (Phase != JxqyGamblePhase.AwaitingBet)
                return false;
            Choice = choice;
            HasChoice = true;
            return true;
        }

        public bool BeginRoll(int die1, int die2, int die3)
        {
            if (Phase != JxqyGamblePhase.AwaitingBet)
                return false;
            if (Stake <= 0)
            {
                ShowNotice("请下注！");
                return false;
            }
            if (!HasChoice)
            {
                ShowNotice("请先选择押大或押小！");
                return false;
            }
            Die1 = ValidateDie(die1, nameof(die1));
            Die2 = ValidateDie(die2, nameof(die2));
            Die3 = ValidateDie(die3, nameof(die3));
            HasRolledDice = true;
            Message = string.Empty;
            Phase = JxqyGamblePhase.Rolling;
            return true;
        }

        public bool BeginOpening()
        {
            if (Phase != JxqyGamblePhase.Rolling)
                return false;
            Phase = JxqyGamblePhase.Opening;
            return true;
        }

        public bool RevealDice()
        {
            if (Phase != JxqyGamblePhase.Opening)
                return false;
            Message = ChineseNumbers[Die1] + ChineseNumbers[Die2] +
                      ChineseNumbers[Die3] + "，" +
                      ChineseNumbers[DiceTotal] +
                      (DiceAreSmall ? "点小" : "点大");
            Phase = JxqyGamblePhase.ResultMessage;
            return true;
        }

        public bool RequestQuit()
        {
            if (Phase != JxqyGamblePhase.AwaitingBet)
                return false;
            if (Opponent != JxqyGambleOpponent.CasinoOwner)
            {
                ShowNotice("无法离开赌局！");
                return false;
            }
            BeginSettlement();
            return true;
        }

        public bool DismissMessage()
        {
            switch (Phase)
            {
                case JxqyGamblePhase.NoticeMessage:
                    Message = string.Empty;
                    Phase = JxqyGamblePhase.AwaitingBet;
                    return true;
                case JxqyGamblePhase.ResultMessage:
                    SettleRound();
                    return true;
                case JxqyGamblePhase.SettlementMessage:
                    Message = string.Empty;
                    Phase = JxqyGamblePhase.Completed;
                    return true;
                default:
                    return false;
            }
        }

        private void SettleRound()
        {
            bool selectedSmall = Choice == JxqyGambleChoice.Small;
            if (selectedSmall == DiceAreSmall)
            {
                AvailableMoney += Stake;
                OpponentMoney -= Stake;
            }
            else
            {
                AvailableMoney -= Stake;
                OpponentMoney += Stake;
                if (AvailableMoney < 0)
                {
                    AvailableMoney += Stake;
                    Stake = AvailableMoney;
                    AvailableMoney = 0;
                }
            }
            if (Stake > OpponentMoney)
            {
                AvailableMoney += Stake - OpponentMoney;
                Stake = OpponentMoney;
            }
            if (OpponentMoney <= 0 || PlayerMoney <= 0)
            {
                BeginSettlement();
                return;
            }
            Message = string.Empty;
            Phase = JxqyGamblePhase.AwaitingBet;
        }

        private void BeginSettlement()
        {
            NetMoneyChange =
                (Stake - OpponentMoney + AvailableMoney) / 2;
            int absolute = Math.Abs(NetMoneyChange);
            Message = NetMoneyChange < 0
                ? $"你赔了{absolute}两银子"
                : $"你赚了{absolute}两银子";
            Phase = JxqyGamblePhase.SettlementMessage;
        }

        private void ShowNotice(string message)
        {
            Message = message ?? string.Empty;
            Phase = JxqyGamblePhase.NoticeMessage;
        }

        private static int ValidateDie(int value, string parameterName)
        {
            if (value < 1 || value > 6)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }
}
