﻿using System;
using System.Collections.Generic;
using System.Linq;
using LeagueSharp;
using LeagueSharp.Common;

namespace TheGaren.Commons.ComboSystem
{
    public class ComboProvider
    {
        protected List<Skill> Skills;
        protected Skill TotalControl;
        private bool _totalControl;
        private bool _cancelUpdate;
        public Obj_AI_Hero Target;
        public readonly Orbwalking.Orbwalker Orbwalker;
        public float TargetRange;
        public TargetSelector.DamageType DamageType;
        public bool AntiGapcloser = true;
        public bool Interrupter = true;
        public Dictionary<string, bool> GapcloserCancel = new Dictionary<string, bool>();
        public readonly Dictionary<string, List<InterruptableSpell>> InterruptableSpells = new Dictionary<string, List<InterruptableSpell>>();
        private readonly List<Tuple<Skill, Action>> _queuedCasts = new List<Tuple<Skill, Action>>();

        public class InterruptableSpell
        {
            public SpellSlot Slot;
            public InterruptableDangerLevel DangerLevel;
            public bool MovementInterrupts;
            public bool FireEvent = true;

            public InterruptableSpell(SpellSlot slot, InterruptableDangerLevel danger, bool movementInterrupts)
            {
                Slot = slot;
                DangerLevel = danger;
                MovementInterrupts = movementInterrupts;
            }
        }

        /// <summary>
        /// Represents a "combo" and it's logic. Manages skill logic.
        /// </summary>
        public ComboProvider(float targetSelectorRange, IEnumerable<Skill> skills, Orbwalking.Orbwalker orbwalker)
        {
            Skills = skills as List<Skill> ?? skills.ToList();
            DamageType = Skills.Count(spell => spell.Spell.DamageType == TargetSelector.DamageType.Magical) > Skills.Count(spell => spell.Spell.DamageType == TargetSelector.DamageType.Physical) ?
                TargetSelector.DamageType.Magical : TargetSelector.DamageType.Physical;
            TargetRange = targetSelectorRange;
            Orbwalker = orbwalker;

            LeagueSharp.Common.AntiGapcloser.Spells.ForEach(spell =>
            {
                var champ = HeroManager.Enemies.FirstOrDefault(enemy => enemy.ChampionName.Equals(spell.ChampionName, StringComparison.InvariantCultureIgnoreCase));
                if (champ != null && !GapcloserCancel.ContainsKey(champ.ChampionName))
                {
                    GapcloserCancel.Add(champ.ChampionName, true);

                }
            });
            LeagueSharp.Common.AntiGapcloser.OnEnemyGapcloser += OnGapcloser;
            InitInterruptable();
            Interrupter2.OnInterruptableTarget += OnInterrupter;
            Game.OnUpdate += _ => UpdateSkills();
            Drawing.OnDraw += _ =>
            {
                foreach (var skill in Skills)
                {
                    skill.Draw();
                }
            };

            Spellbook.OnCastSpell += (sender, args) =>
            {
                if (!sender.Owner.IsMe) return;
                for (int i = 0; i < _queuedCasts.Count; i++)
                {
                    if (_queuedCasts[i].Item1.Spell.Slot == args.Slot)
                    {
                        _queuedCasts.RemoveAt(i);
                        break;
                    }
                }
            };
        }

        public ComboProvider(float targetSelectorRange, Orbwalking.Orbwalker orbwalker, params Skill[] skills)
            : this(targetSelectorRange, skills.ToList(), orbwalker) { }

        public void CreateBasicMenu(Menu comboMenu, Menu harassMenu, Menu laneclearMenu, Menu antiGapcloserMenu, Menu interrupterMenu, Menu manamanagerMenu, Menu ignitemanagerMenu, /*Menu healMenu,*/ Menu itemMenu, bool laneclearHarassSwitch = true /*bool healmanager = true,*/)
        {
            if (comboMenu != null)
            {
                CreateComboMenu(comboMenu);
            }

            if (harassMenu != null)
            {
                CreateHarassMenu(harassMenu);
            }

            if (laneclearMenu != null)
            {
                CreateLaneclearMenu(laneclearMenu, laneclearHarassSwitch);
            }

            if (antiGapcloserMenu != null)
            {
                var gapcloserSpells = new Menu("Enemies", "Gapcloser.Enemies");
                AddGapclosersToMenu(gapcloserSpells);
                antiGapcloserMenu.AddSubMenu(gapcloserSpells);
                antiGapcloserMenu.AddMItem("Enabled", true, (sender, args) => AntiGapcloser = args.GetNewValue<bool>());
            }

            if (interrupterMenu != null)
            {
                var spellMenu = new Menu("Spells", "Interrupter.Spells");
                AddInterruptablesToMenu(spellMenu);
                interrupterMenu.AddSubMenu(spellMenu);
                interrupterMenu.AddMItem("Enabled", true, (sender, args) => Interrupter = args.GetNewValue<bool>());
            }

            if (manamanagerMenu != null)
            {
                ManaManager.Initialize(manamanagerMenu);
            }

            if (ignitemanagerMenu != null)
            {
                IgniteManager.Initialize(ignitemanagerMenu, this, true);
            }

            //if (healmanager)
            //{
            //    HealManager.Initialize(healMenu, this);
            //}

            if (itemMenu != null)
            {
                ItemManager.Initialize(itemMenu, this);
            }
        }

        public void CreateComboMenu(Menu comboMenu, params SpellSlot[] forbiddenSlots)
        {
            foreach (var skill in Skills)
            {
                Skill currentSkill = skill;
                if (forbiddenSlots.Contains(currentSkill.Spell.Slot)) continue;
                comboMenu.AddMItem("Use " + skill.Spell.Slot, skill.ComboEnabled, (sender, args) => SetEnabled(currentSkill, Orbwalking.OrbwalkingMode.Combo, args.GetNewValue<bool>()));
                if (skill.Spell.IsSkillshot)
                    comboMenu.AddMItem(skill.Spell.Slot + " Hitchance", new StringList(new[] { "Low", "Medium", "High", "VeryHigh" }), (sender, args) => currentSkill.SetMinComboHitchance(args.GetNewValue<StringList>().SelectedValue));
            }
            comboMenu.ProcStoredValueChanged<bool>();
            comboMenu.ProcStoredValueChanged<StringList>();
        }

        public void CreateHarassMenu(Menu harassMenu, params SpellSlot[] forbiddenSlots)
        {
            foreach (var skill in Skills)
            {
                Skill currentSkill = skill;
                if (forbiddenSlots.Contains(currentSkill.Spell.Slot) || currentSkill.Spell.Slot == SpellSlot.R) continue;
                harassMenu.AddMItem("Use " + skill.Spell.Slot, skill.HarassEnabled, (sender, args) => SetEnabled(currentSkill, Orbwalking.OrbwalkingMode.Mixed, args.GetNewValue<bool>()));
                if (skill.Spell.IsSkillshot)
                    harassMenu.AddMItem(skill.Spell.Slot + " Hitchance", new StringList(new[] { "Low", "Medium", "High", "VeryHigh" }) { SelectedIndex = 3 }, (sender, args) => currentSkill.SetMinHarassHitchance(args.GetNewValue<StringList>().SelectedValue));
            }
            harassMenu.ProcStoredValueChanged<bool>();
            harassMenu.ProcStoredValueChanged<StringList>();
        }

        public void CreateLaneclearMenu(Menu laneclearMenu, bool harassSwitch = true, params SpellSlot[] forbiddenSlots)
        {
            foreach (var skill in Skills)
            {
                Skill currentSkill = skill;
                if (forbiddenSlots.Contains(currentSkill.Spell.Slot) || currentSkill.Spell.Slot == SpellSlot.R) continue;
                laneclearMenu.AddMItem("Use " + skill.Spell.Slot, skill.LaneclearEnabled, (sender, args) => SetEnabled(currentSkill, Orbwalking.OrbwalkingMode.LaneClear, args.GetNewValue<bool>()));
            }
            if (harassSwitch) laneclearMenu.AddMItem("Harass instead if enemy near", false, (sender, args) => GetSkills().ToList().ForEach(skill => skill.SwitchClearToHarassOnTarget = args.GetNewValue<bool>()));

            laneclearMenu.ProcStoredValueChanged<bool>();
        }

        private void InitInterruptable()
        {
            //Interrupter2 contains the spells, but they are private. Can't use reflection cause of sandbox. GG WP
            RegisterInterruptableSpell("Caitlyn", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("FiddleSticks", new InterruptableSpell(SpellSlot.W, InterruptableDangerLevel.Medium, true));
            RegisterInterruptableSpell("FiddleSticks", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Galio", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Janna", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.Low, true));
            RegisterInterruptableSpell("Karthus", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Katarina", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Lucian", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, false));
            RegisterInterruptableSpell("Malzahar", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("MasterYi", new InterruptableSpell(SpellSlot.W, InterruptableDangerLevel.Low, true));
            RegisterInterruptableSpell("MissFortune", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Nunu", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Pantheon", new InterruptableSpell(SpellSlot.E, InterruptableDangerLevel.Low, true));
            RegisterInterruptableSpell("Pantheon", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("RekSai", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Sion", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.Low, true));
            RegisterInterruptableSpell("Shen", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.Low, true));
            RegisterInterruptableSpell("TwistedFate", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.Medium, true));
            RegisterInterruptableSpell("Urgot", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Velkoz", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Warwick", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Xerath", new InterruptableSpell(SpellSlot.R, InterruptableDangerLevel.High, true));
            RegisterInterruptableSpell("Varus", new InterruptableSpell(SpellSlot.Q, InterruptableDangerLevel.Low, false));
        }

        private void RegisterInterruptableSpell(string name, InterruptableSpell spell)
        {
            if (!InterruptableSpells.ContainsKey(name))
                InterruptableSpells.Add(name, new List<InterruptableSpell>());
            if (HeroManager.Enemies.Any(enemy => enemy.ChampionName == name))
                InterruptableSpells[name].Add(spell);
        }

        /// <summary>
        /// Note: Do not use autoattacks as additionalSpellDamage!
        /// </summary>
        /// <param name="target"></param>
        /// <param name="additionalSpellDamage"></param>
        /// <returns></returns>
        public virtual bool ShouldBeDead(Obj_AI_Hero target, float additionalSpellDamage = 0f)
        {
            var healthPred = HealthPrediction.GetHealthPrediction(target, 1000);
            return healthPred - (IgniteManager.GetRemainingDamage(target) + additionalSpellDamage) <= 0;
        }

        private void OnInterrupter(Obj_AI_Hero sender, Interrupter2.InterruptableTargetEventArgs args)
        {
            if (!Interrupter || sender.IsAlly) return;
            var interruptableSpell = InterruptableSpells[sender.ChampionName].FirstOrDefault(interruptable => interruptable.Slot == sender.Spellbook.ActiveSpellSlot || (sender.Spellbook.ActiveSpellSlot == SpellSlot.Unknown && interruptable.DangerLevel.ToString() == args.DangerLevel.ToString()));
            if (interruptableSpell == null || !interruptableSpell.FireEvent)
                return;

            foreach (var skill in Skills)
                skill.Interruptable(this, sender, interruptableSpell, args.EndTime);
        }

        public void AddGapclosersToMenu(Menu menu)
        {
            GapcloserCancel.Keys.ToList().ForEach(item => menu.AddMItem(item, true, (sender, args) => GapcloserCancel[item] = args.GetNewValue<bool>()));
        }

        public void AddInterruptablesToMenu(Menu menu)
        {
            InterruptableSpells.ToList().ForEach(pair => pair.Value.ForEach(champSpell => menu.AddMItem(pair.Key + ": " + champSpell.Slot.ToString(), champSpell.FireEvent, (sender, args) => champSpell.FireEvent = args.GetNewValue<bool>())));
        }

        private void OnGapcloser(ActiveGapcloser gapcloser)
        {
            //            Game.PrintChat("try " + gapcloser.Sender.ChampionName + " have: " + GapcloserCancel.FirstOrDefault().Key);

            if (!AntiGapcloser || !GapcloserCancel[gapcloser.Sender.ChampionName] || gapcloser.Sender.IsAlly) return;
            foreach (var skill in Skills)
                skill.Gapcloser(this, gapcloser);
        }

        /// <summary>
        /// call to init all stuffs. Menu has to exist at that time
        /// </summary>
        public virtual void Initialize()
        {
            Skills.ForEach(skill => skill.Initialize(this));
        }

        public float GetComboDamage(Obj_AI_Hero enemy)
        {
            return Skills.Sum(skill => skill.ComboEnabled ? skill.GetDamage(enemy) : 0);
        }

        public Obj_AI_Hero GetTarget()
        {
            return Target;
        }

        /// <summary>
        /// override in sub class to add champion combo logic. for example Garen has a fixed combo, but wants to do W not in order, but when he gets damage.
        /// In this example you would override Update and have a seperate logic for W instead of adding it to the skill routines.
        /// </summary>
        public virtual void Update()
        {
            Target = TargetSelector.GetTarget(TargetRange, DamageType);
            UpdateSkills();

            for (int i = _queuedCasts.Count - 1; i >= 0; i--)
            {
                if (_queuedCasts[i].Item1.HasBeenCast()) _queuedCasts.RemoveAt(i);
                else
                {
                    _queuedCasts[i].Item2();
                    break;
                }
            }
        }

        public void SetCurrentSkillCast()
        {
            if (_queuedCasts.Count > 0) _queuedCasts.RemoveAt(_queuedCasts.Count - 1);
        }

        private void UpdateSkills()
        {
            Skills.Sort(); //Checked: this is not expensive

            if (_totalControl)
            {
                TotalControl.Update(Orbwalker.ActiveMode, this, Target);

                if (!TotalControl.NeedsControl())
                {
                    TotalControl.TryTerminate();
                    _totalControl = false;
                    UpdateSkills();
                    return;
                }

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var skill in Skills.Where(item => item.GetPriority() > TotalControl.GetPriority()))
                {
                    skill.Update(Orbwalker.ActiveMode, this, Target);
                }
            }
            else
            {
                foreach (var item in Skills)
                {
                    if (_cancelUpdate)
                    {
                        _cancelUpdate = false;
                        return;
                    }
                    item.Update(Orbwalker.ActiveMode, this, Target);
                }
            }

        }

        public void AddCastAction(Skill skill, Action action)
        {
            for (int i = 0; i < _queuedCasts.Count; i++)
                if (_queuedCasts[i].Item1 == skill) return;
            _queuedCasts.Add(new Tuple<Skill, Action>(skill, action));
        }
        
        public bool GrabControl(Skill skill)
        {
            if (_totalControl && TotalControl == skill)
                return true;
            if (_totalControl && TotalControl.GetPriority() < skill.GetPriority())
            {
                TotalControl.TryTerminate();
                TotalControl = skill;
                _cancelUpdate = true;
                return true;
            }
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var currentSkill in Skills)
            {
                if (skill != currentSkill && currentSkill.NeedsControl() && currentSkill.GetEnabled(Orbwalker.ActiveMode) && currentSkill.GetPriority() >= skill.GetPriority())
                {
                    return false;
                }
            }
            _totalControl = true;
            TotalControl = skill;
            _cancelUpdate = true;
            return true;
        }

        public void SetEnabled(Skill skill, Orbwalking.OrbwalkingMode mode, bool enabled)
        {
            skill.SetEnabled(mode, enabled);
        }

        public void SetEnabled<T>(Orbwalking.OrbwalkingMode mode, bool enabled) where T : Skill
        {
            foreach (var skill in Skills.Where(skill => skill.GetType() == typeof(T)))
            {
                skill.SetEnabled(mode, enabled);
            }
        }

        public T GetSkill<T>() where T : Skill
        {
            return (T)Skills.FirstOrDefault(skill => skill is T);
        }

        public Skill[] GetSkills()
        {
            return Skills.ToArray();
        }
    }
}
