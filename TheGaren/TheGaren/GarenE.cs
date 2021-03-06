﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;
using TheGaren.Commons;
using TheGaren.Commons.ComboSystem;
using TheGaren.Commons.Items;

namespace TheGaren
{
    class GarenE : Skill
    {
        public bool OnlyAfterAuto;
        public int MinFarmMinions;
        public bool UseHydra;
        private bool _recentAutoattack;
        private bool _resetOrbwalker;
        private GarenQ _q;
        private GarenR _r;

        public GarenE(Spell spell)
            : base(spell)
        {
            HarassEnabled = false;
            Orbwalking.AfterAttack += OnAfterAttack;
            OnlyUpdateIfTargetValid = false;
            OnlyUpdateIfCastable = false;
        }

        public override void Initialize(ComboProvider combo)
        {
            _q = combo.GetSkill<GarenQ>();
            _r = combo.GetSkill<GarenR>();
            base.Initialize(combo);
        }

        private void OnAfterAttack(AttackableUnit unit, AttackableUnit target)
        {
            _recentAutoattack = true;
        }

        public override void Update(Orbwalking.OrbwalkingMode mode, ComboProvider combo, Obj_AI_Hero target)
        {
            base.Update(mode, combo, target);
            _recentAutoattack = false;
            if (_resetOrbwalker && !ObjectManager.Player.HasBuff("GarenE"))
            {
                _resetOrbwalker = false;
                Provider.Orbwalker.SetAttack(true);
            }
        }

        public override void Cast(Obj_AI_Hero target, bool force = false)
        {
            if (!CanBeCast()) return;
            if (_r.CanBeCast() && Spell.Instance.Name != "GarenE" && target.IsValidTarget() && _r.Spell.IsKillable(target))
            {
                SafeCast();
                return;
            }
            if ((_q.Spell.GetState() == SpellState.Cooldown || _q.Spell.GetState() == SpellState.NotLearned) && !ObjectManager.Player.HasBuff("GarenQ") && (!OnlyAfterAuto || !AAHelper.WillAutoattackSoon || _recentAutoattack) && HeroManager.Enemies.Any(enemy => enemy.IsValidTarget() && Spell.Instance.Name == "GarenE" && enemy.Position.Distance(ObjectManager.Player.Position) < 325))
            {
                Provider.Orbwalker.SetAttack(false);
                _resetOrbwalker = true;
                SafeCast();
            }
        }

        public override void LaneClear(ComboProvider combo, Obj_AI_Hero target)
        {
            if (MinionManager.GetMinions(325, MinionTypes.All, MinionTeam.NotAlly, MinionOrderTypes.None).Count >= MinFarmMinions)
            {
                if (!ObjectManager.Player.HasBuff("GarenQ") && Spell.Instance.Name == "GarenE")
                    SafeCast();
                if (UseHydra) ItemManager.GetItem<RavenousHydra>().Use(null);
            }
        }

        public override int GetPriority()
        {
            return ObjectManager.Player.HasBuff("GarenE") ? 3 : 1;
        }
    }
}
