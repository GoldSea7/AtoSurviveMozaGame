﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SurvivalEngine
{

    /// <summary>
    /// Main character script, contains code for movement and for player controls/commands
    /// </summary>

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerCharacterCombat))]
    [RequireComponent(typeof(PlayerCharacterAttribute))]
    [RequireComponent(typeof(PlayerCharacterInventory))]
    [RequireComponent(typeof(PlayerCharacterCraft))]
    public class PlayerCharacter : MonoBehaviour
    {
        public int player_id = 0;

        [Header("Movement")]
        public float move_speed = 4f;
        public float move_accel = 8;
        public float rotate_speed = 180f;
        public float fall_speed = 20f; //Falling speed
        public float fall_gravity = 40f; //Falling acceleration
        public float slope_angle_max = 45f; //Maximum angle, in degrees that the character can climb up
        public float moving_threshold = 0.15f; //Move threshold is how fast the character need to move before its considered movement (triggering animations, etc)
        public float ground_detect_dist = 0.1f; //Margin distance between the character and the ground, used to detect if character is grounded.
        public LayerMask ground_layer = ~0; //What is considered ground?
        public bool use_navmesh = false;

        public UnityAction<string, float> onTriggerAnim;

        private Rigidbody rigid;
        private CapsuleCollider collide;
        private PlayerCharacterAttribute character_attr;
        private PlayerCharacterCombat character_combat;
        private PlayerCharacterCraft character_craft;
        private PlayerCharacterInventory character_inventory;
        private PlayerCharacterJump character_jump;
        private PlayerCharacterSwim character_swim;
        private PlayerCharacterClimb character_climb;
        private PlayerCharacterRide character_ride;
        private PlayerCharacterAnim character_anim;

        private Vector3 move;
        private Vector3 facing;
        private Vector3 move_average;
        private Vector3 prev_pos;
        private Vector3 fall_vect;

        private bool auto_move = false;
        private Vector3 auto_move_target;
        private Vector3 auto_move_target_next;
        private Selectable auto_move_select = null;
        private Destructible auto_move_attack = null;

        private int auto_move_drop = -1;
        private InventoryData auto_move_drop_inventory;
        private float auto_move_timer = 0f;

        private Vector3 ground_normal = Vector3.up;
        private bool controls_enabled = true;
        private bool movement_enabled = true;

        private bool is_grounded = false;
        private bool is_fronted = false;
        private bool is_action = false;
        private bool is_sleep = false;
        private bool is_fishing = false;

        private ActionSleep sleep_target = null;
        private Coroutine action_routine = null;
        private GameObject action_progress = null;
        private bool can_cancel_action = false;

        private Vector3[] nav_paths = new Vector3[0];
        private int path_index = 0;
        private bool calculating_path = false;
        private bool path_found = false;

        private static PlayerCharacter player_first = null;
        private static List<PlayerCharacter> players_list = new List<PlayerCharacter>();

        void Awake()
        {
            if (player_first == null || player_id < player_first.player_id)
                player_first = this;

            players_list.Add(this);
            rigid = GetComponent<Rigidbody>();
            collide = GetComponentInChildren<CapsuleCollider>();
            character_attr = GetComponent<PlayerCharacterAttribute>();
            character_combat = GetComponent<PlayerCharacterCombat>();
            character_craft = GetComponent<PlayerCharacterCraft>();
            character_inventory = GetComponent<PlayerCharacterInventory>();
            character_jump = GetComponent<PlayerCharacterJump>();
            character_swim = GetComponent<PlayerCharacterSwim>();
            character_climb = GetComponent<PlayerCharacterClimb>();
            character_ride = GetComponent<PlayerCharacterRide>();
            character_anim = GetComponent<PlayerCharacterAnim>();
            facing = transform.forward;
            prev_pos = transform.position;
            fall_vect = Vector3.down * fall_speed;
        }

        private void OnDestroy()
        {
            players_list.Remove(this);
        }

        private void Start()
        {
            PlayerControlsMouse mouse_controls = PlayerControlsMouse.Get();
            mouse_controls.onClickFloor += OnClickFloor;
            mouse_controls.onClickObject += OnClickObject;
            mouse_controls.onClick += OnClick;
            mouse_controls.onRightClick += OnRightClick;
            mouse_controls.onHold += OnMouseHold;
            mouse_controls.onRelease += OnMouseRelease;

            TheGame.Get().onPause += OnPause;

            if (player_id < 0)
                Debug.LogError("Player ID should be 0 or more: -1 is reserved to indicate neutral (no player)");
        }

        private void Update()
        {
            if (TheGame.Get().IsPaused())
                return;

            if (IsDead())
                return;

            //Save position
            Data.position = GetPosition();

            //Controls
            PlayerControls controls = PlayerControls.Get(player_id);

            //Activate Selectable when near
            Vector3 move_dir = auto_move_target - transform.position;
            if (auto_move && !is_action && auto_move_select != null && move_dir.magnitude < auto_move_select.use_range)
            {
                auto_move = false;
                auto_move_select.Use(this, auto_move_target);
                auto_move_select = null;
            }

            //Finish construction when near clicked spot
            Buildable current_buildable = character_craft.GetCurrentBuildable();
            if (auto_move && !is_action && character_craft.ClickedBuild() && current_buildable != null && move_dir.magnitude < current_buildable.build_distance)
            {
                auto_move = false;
                character_craft.StartCraftBuilding(auto_move_target);
            }

            //Stop move & drop when near clicked spot
            if (auto_move && !is_action && move_dir.magnitude < moving_threshold * 2f)
            {
                auto_move = false;
                character_inventory.DropItem(auto_move_drop_inventory, auto_move_drop);
            }

            //Stop attacking if target cant be attacked anymore (tool broke, or target died...)
            if (!character_combat.CanAttack(auto_move_attack))
                auto_move_attack = null;

            //Controls
            if (IsControlsEnabled())
            {
                //Check if panel is focused
                KeyControlsUI ui_controls = KeyControlsUI.Get(player_id);
                bool panel_focus = controls.gamepad_controls && ui_controls != null && ui_controls.IsPanelFocus();
                if (!panel_focus && !is_action)
                {
                    //Press Action button
                    if (controls.IsPressAction())
                    {
                        if (character_craft.CanBuild())
                            character_craft.StartCraftBuilding();
                        else
                            InteractWithNearest();
                    }

                    //Press attack
                    if (Combat.can_attack && controls.IsPressAttack())
                        AttackNearest();

                    //Press jump
                    if (character_jump != null && controls.IsPressJump())
                        character_jump.Jump();
                }

                if (controls.IsPressUISelect() && !is_action && character_craft.CanBuild())
                    character_craft.StartCraftBuilding();
            }
        }

        void FixedUpdate()
        {
            if (TheGame.Get().IsPaused())
                return;

            if (IsDead())
                return;

            PlayerControls controls = PlayerControls.Get(player_id);
            PlayerControlsMouse mcontrols = PlayerControlsMouse.Get();

            //Update the automove target position based on navmesh path, or moving target
            UpdateAutoMoveTarget();

            //Check if grounded
            DetectGrounded();
            DetectFronted();

            //Find the direction the character should move
            Vector3 tmove = FindMovementDirection();

            //Apply the move calculated previously
            move = Vector3.Lerp(move, tmove, move_accel * Time.fixedDeltaTime);
            rigid.velocity = move;

            //Find facing direction
            Vector3 tfacing = FindFacingDirection();
            if (tfacing.magnitude > 0.5f)
                facing = tfacing;

            //Apply the facing
            Quaternion targ_rot = Quaternion.LookRotation(facing, Vector3.up);
            rigid.MoveRotation(Quaternion.RotateTowards(rigid.rotation, targ_rot, rotate_speed * Time.fixedDeltaTime));

            //Check the average traveled movement (allow to check if character is stuck)
            Vector3 last_frame_travel = transform.position - prev_pos;
            move_average = Vector3.MoveTowards(move_average, last_frame_travel, 1f * Time.fixedDeltaTime);
            prev_pos = transform.position;

            //Stop auto move
            bool stuck_somewhere = move_average.magnitude < 0.02f && auto_move_timer > 1f;
            if (stuck_somewhere)
                auto_move = false;

            //Stop the click auto move when moving with keyboard/joystick/gamepad
            if (controls.IsMoving() || mcontrols.IsJoystickActive() || mcontrols.IsDoubleTouch())
                StopAutoMove();

            //Cancel action if moving
            bool is_moving_controls = auto_move || controls.IsMoving() || mcontrols.IsJoystickActive();
            if (is_action && can_cancel_action && is_moving_controls)
                CancelAction();

            //Stop sleep
            if (is_action || IsMoving() || sleep_target == null)
                StopSleep();
        }

        private void UpdateAutoMoveTarget()
        {
            //Update auto move for moving targets
            GameObject auto_move_obj = GetAutoTarget();
            if (auto_move && auto_move_obj != null)
            {
                Vector3 diff = auto_move_obj.transform.position - auto_move_target;
                if (diff.magnitude > 1f)
                {
                    auto_move_target = auto_move_obj.transform.position;
                    auto_move_target_next = auto_move_obj.transform.position;
                    CalculateNavmesh(); //Recalculate navmesh because target moved
                }
            }

            //Navmesh calculate next path
            if (auto_move && use_navmesh && path_found && path_index < nav_paths.Length)
            {
                auto_move_target_next = nav_paths[path_index];
                Vector3 move_dir_total = auto_move_target_next - transform.position;
                move_dir_total.y = 0f;
                if (move_dir_total.magnitude < 0.2f)
                    path_index++;
            }
        }

        private Vector3 FindMovementDirection()
        {
            PlayerControls controls = PlayerControls.Get(player_id);
            PlayerControlsMouse mcontrols = PlayerControlsMouse.Get();
            Vector3 tmove = Vector3.zero;

            if (!IsMovementEnabled())
                return tmove;

            //AUTO Moving (after mouse click)
            auto_move_timer += Time.fixedDeltaTime;
            if (auto_move && auto_move_timer > 0.02f) //auto_move_timer to let the navmesh time to calculate a path
            {
                Vector3 move_dir_total = auto_move_target - transform.position;
                Vector3 move_dir_next = auto_move_target_next - transform.position;
                Vector3 move_dir = move_dir_next.normalized * Mathf.Min(move_dir_total.magnitude, 1f);
                move_dir.y = 0f;

                float move_dist = Mathf.Min(GetMoveSpeed(), move_dir.magnitude * 10f);
                tmove = move_dir.normalized * move_dist;
            }

            //Keyboard/gamepad moving
            if (!auto_move && IsControlsEnabled())
            {
                Vector3 cam_move = TheCamera.Get().GetRotation() * controls.GetMove();
                if (mcontrols.IsJoystickActive() && !character_craft.IsBuildMode())
                {
                    Vector2 joystick = mcontrols.GetJoystickDir();
                    cam_move = TheCamera.Get().GetRotation() * new Vector3(joystick.x, 0f, joystick.y);
                }
                tmove = cam_move * GetMoveSpeed();
            }

            //Stop moving if doing action
            if (is_action)
                tmove = Vector3.zero;

            if (!is_grounded || IsJumping())
            {
                if (!IsJumping())
                    fall_vect = Vector3.MoveTowards(fall_vect, Vector3.down * fall_speed, fall_gravity * Time.fixedDeltaTime);
                tmove += fall_vect;
            }
            //Add slope angle
            else if (is_grounded)
            {
                tmove = Vector3.ProjectOnPlane(tmove.normalized, ground_normal).normalized * tmove.magnitude;
            }

            return tmove;
        }

        private Vector3 FindFacingDirection()
        {
            PlayerControls controls = PlayerControls.Get(player_id);
            Vector3 tfacing = Vector3.zero;

            if (!IsMovementEnabled())
                return tfacing;

            //Calculate Facing
            if (IsMoving())
            {
                tfacing = new Vector3(move.x, 0f, move.z).normalized;
            }

            //Rotate character with right joystick when not in free rotate mode
            bool freerotate = TheCamera.Get().IsFreeRotation();
            if (!freerotate && controls.IsGamePad())
            {
                Vector2 look = controls.GetFreelook();
                Vector3 look3 = TheCamera.Get().GetRotation() * new Vector3(look.x, 0f, look.y);
                if (look3.magnitude > 0.5f)
                    tfacing = look3.normalized;
            }

            return tfacing;
        }

        private void OnPause(bool paused)
        {
            if (paused)
            {
                rigid.velocity = Vector3.zero;
            }
        }

        //Detect if character is on the floor
        private void DetectGrounded()
        {
            float hradius = GetColliderHeightRadius();
            float radius = GetColliderRadius() * 0.9f;
            Vector3 center = GetColliderCenter();

            float gdist; Vector3 gnormal;
            is_grounded = PhysicsTool.DetectGround(transform, center, hradius, radius, ground_layer, out gdist, out gnormal);
            ground_normal = gnormal;

            float slope_angle = Vector3.Angle(ground_normal, Vector3.up);
            is_grounded = is_grounded && slope_angle <= slope_angle_max;
        }

        //Detect if there is an obstacle in front of the character
        private void DetectFronted()
        {
            Vector3 scale = transform.lossyScale;
            float hradius = collide.height * scale.y * 0.5f - 0.02f; //radius is half the height minus offset
            float radius = collide.radius * (scale.x + scale.y) * 0.5f + 0.5f;

            Vector3 center = GetColliderCenter();
            Vector3 p1 = center;
            Vector3 p2 = center + Vector3.up * hradius;
            Vector3 p3 = center + Vector3.down * hradius;

            RaycastHit h1, h2, h3;
            bool f1 = PhysicsTool.RaycastCollision(p1, facing * radius, out h1);
            bool f2 = PhysicsTool.RaycastCollision(p2, facing * radius, out h2);
            bool f3 = PhysicsTool.RaycastCollision(p3, facing * radius, out h3);

            is_fronted = f1 || f2 || f3;

            //Debug.DrawRay(p1, facing * radius);
            //Debug.DrawRay(p2, facing * radius);
            //Debug.DrawRay(p3, facing * radius);
        }

        //--- Generic Actions ----

        //Same as trigger action, but also show the progress circle
        public void TriggerProgressAction(float duration, UnityAction callback = null)
        {
            if (!is_action)
            {
                if (AssetData.Get().action_progress != null && duration > 0.1f)
                {
                    action_progress = Instantiate(AssetData.Get().action_progress, transform);
                    action_progress.GetComponent<ActionProgress>().duration = duration;
                }

                action_routine = StartCoroutine(RunActionRoutine(duration, callback));
                can_cancel_action = true;
                StopMove();
            }
        }

        //Wait for X seconds for any generic action (player can't do other things during that time)
        public void TriggerAction(float duration, UnityAction callback = null)
        {
            if (!is_action)
            {
                action_routine = StartCoroutine(RunActionRoutine(duration, callback));
                can_cancel_action = false;
            }
        }

        private IEnumerator RunActionRoutine(float action_duration, UnityAction callback=null)
        {
            is_action = true;

            yield return new WaitForSeconds(action_duration);

            is_action = false;
            if (callback != null)
                callback.Invoke();
        }

        public void CancelAction()
        {
            if (action_routine != null)
                StopCoroutine(action_routine);
            if (action_progress != null)
                Destroy(action_progress);
            is_action = false;
            is_fishing = false;
        }

        //Call animation directly
        public void TriggerAnim(string anim, Vector3 face_at, float duration = 0f)
        {
            FaceTorward(face_at);
            if (onTriggerAnim != null)
                onTriggerAnim.Invoke(anim, duration);
        }

        public void SetDoingAction(bool action)
        {
            is_action = action;
            can_cancel_action = false;
        }

        //---- Special actions

        public void Sleep(ActionSleep sleep_target)
        {
            if (!is_sleep && IsMovementEnabled())
            {
                this.sleep_target = sleep_target;
                is_sleep = true;
                auto_move = false;
                auto_move_attack = null;
                TheGame.Get().SetGameSpeedMultiplier(sleep_target.sleep_speed_mult);
            }
        }

        public void StopSleep()
        {
            if (is_sleep)
            {
                is_sleep = false;
                sleep_target = null;
                TheGame.Get().SetGameSpeedMultiplier(1f);
            }
        }

        //Fish item from a fishing spot
        public void FishItem(ItemProvider source, int quantity)
        {
            if (source != null && source.HasItem())
            {
                is_fishing = true;

                if (source != null)
                    FaceTorward(source.transform.position);

                TriggerAction(0.4f, () =>
                {
                    action_routine = StartCoroutine(FishRoutine(source, quantity));
                });
            }
        }

        private IEnumerator FishRoutine(ItemProvider source, int quantity)
        {
            is_fishing = true;

            float timer = 0f;
            while (is_fishing && timer < 3f)
            {
                yield return new WaitForSeconds(0.02f);
                timer += 0.02f;

                if (IsMoving())
                    is_fishing = false;
            }

            if (is_fishing)
            {
                source.RemoveItem();
                source.GainItem(this, quantity);
            }

            is_fishing = false;
        }

        //----- Player Orders ----------

        public void MoveTo(Vector3 pos)
        {
            auto_move = true;
            auto_move_target = pos;
            auto_move_target_next = pos;
            auto_move_select = null;
            auto_move_attack = null;
            auto_move_drop = -1;
            auto_move_drop_inventory = null;
            auto_move_timer = 0f;
            path_found = false;
            calculating_path = false;

            CalculateNavmesh();
        }

        public void UpdateMoveTo(Vector3 pos)
        {
            //Meant to be called every frame, for this reason don't do navmesh
            auto_move = true;
            auto_move_target = pos;
            auto_move_target_next = pos;
            path_found = false;
            calculating_path = false;
            auto_move_select = null;
            auto_move_attack = null;
            auto_move_drop = -1;
            auto_move_drop_inventory = null;
        }

        public void FaceTorward(Vector3 pos)
        {
            Vector3 face = (pos - transform.position);
            face.y = 0f;
            if (face.magnitude > 0.01f)
            {
                facing = face.normalized;
            }
        }

        public void InteractWith(Selectable selectable, Vector3 pos)
        {
            bool can_interact = selectable.CanBeInteracted();
            Vector3 tpos = selectable.GetClosestInteractPoint(transform.position, pos);

            auto_move_select = can_interact ? selectable : null;
            auto_move_target = tpos;
            auto_move_target_next = tpos;

            auto_move = true;
            auto_move_drop = -1;
            auto_move_drop_inventory = null;
            auto_move_timer = 0f;
            path_found = false;
            calculating_path = false;
            auto_move_attack = null;

            character_craft.CancelCrafting();
            CalculateNavmesh();
        }

        public void Attack(Destructible target)
        {
            if (character_combat.CanAttack(target))
            {
                auto_move = true;
                auto_move_select = null;
                auto_move_attack = target;
                auto_move_target = target.transform.position;
                auto_move_target_next = target.transform.position;
                auto_move_drop = -1;
                auto_move_drop_inventory = null;
                auto_move_timer = 0f;
                path_found = false;
                calculating_path = false;

                character_craft.CancelCrafting();
                CalculateNavmesh();
            }
        }

        //Shoot arrow in facing direction
        public void AttackRanged()
        {
            Combat.DoAttackNoTarget();
        }

        public void InteractWithNearest()
        {
            Selectable nearest = Selectable.GetNearestAutoInteract(transform.position, 4f);
            if (nearest != null)
            {
                InteractWith(nearest, nearest.GetClosestInteractPoint(transform.position));
            }
        }

        public void AttackNearest()
        {
            Destructible destruct = Destructible.GetNearestAutoAttack(this, transform.position, 4f);
            if (Combat.HasRangedProjectile())
            {
                AttackRanged();
            }
            else if (destruct != null)
            {
                Attack(destruct);
            }
        }

        public void StopMove()
        {
            StopAutoMove();
            move = Vector3.zero;
            rigid.velocity = Vector3.zero;
        }

        public void StopAutoMove()
        {
            auto_move = false;
            auto_move_select = null;
            auto_move_attack = null;
            auto_move_drop_inventory = null;
        }

        //Temporary pause auto move to be resumed (but keep its target)
        public void PauseAutoMove()
        {
            auto_move = false;
        }

        public void ResumeAutoMove()
        {
            if (auto_move_select != null || auto_move_attack != null)
                auto_move = true;
        }

        public void SetFallVect(Vector3 fall)
        {
            fall_vect = fall;
        }

        public void Kill()
        {
            character_combat.Kill();
        }

        public void EnableControls()
        {
            controls_enabled = true;
        }

        public void DisableControls()
        {
            controls_enabled = false;
            StopAutoMove();
        }

        public void EnableMovement()
        {
            movement_enabled = true;
        }

        public void DisableMovement()
        {
            movement_enabled = false;
            StopAutoMove();
        }

        public void EnableCollider()
        {
            collide.enabled = true;
        }

        public void DisableCollider()
        {
            collide.enabled = false;
        }

        //------- Mouse Clicks --------

        private void OnClick(Vector3 pos)
        {
            if (!controls_enabled)
                return;

        }

        private void OnRightClick(Vector3 pos)
        {
            if (!controls_enabled)
                return;

        }

        private void OnMouseHold(Vector3 pos)
        {
            if (!controls_enabled)
                return;

            if (TheGame.IsMobile())
                return; //On mobile, use joystick instead, no mouse hold

            //Stop auto target if holding
            PlayerControlsMouse mcontrols = PlayerControlsMouse.Get();
            if (auto_move && mcontrols.GetMouseHoldDuration() > 1f)
                StopAutoMove();

            //Only hold for normal movement, if interacting dont change while holding
            if (character_craft.GetCurrentBuildable() == null && auto_move_select == null && auto_move_attack == null)
            {
                UpdateMoveTo(pos);
            }
        }

        private void OnMouseRelease(Vector3 pos)
        {
            if (TheGame.IsMobile())
            {
                character_craft.TryBuildAt(pos);
            }
        }

        private void OnClickFloor(Vector3 pos)
        {
            if (!controls_enabled)
                return;

            if (is_action && can_cancel_action)
                CancelAction();

            //Cancel previous build
            if (character_craft.ClickedBuild())
                character_craft.CancelCrafting();

            //Build mode
            if (character_craft.IsBuildMode())
            {
                if(!TheGame.IsMobile()) //On mobile, will build on mouse release
                    character_craft.TryBuildAt(pos);
            }
            //Move to clicked position
            else
            {
                MoveTo(pos);

                PlayerUI ui = PlayerUI.Get(player_id);
                auto_move_drop = ui != null ? ui.GetSelectedSlotIndex() : -1;
                auto_move_drop_inventory = ui != null ? ui.GetSelectedSlotInventory() : null;
            }
        }

        private void OnClickObject(Selectable selectable, Vector3 pos)
        {
            if (!controls_enabled)
                return;

            if (selectable == null)
                return;

            if (character_craft.IsBuildMode())
            {
                OnClickFloor(pos);
                return;
            }

            if (is_action && can_cancel_action)
                CancelAction();

            selectable.Select();

            //Attack target ?
            Destructible target = selectable.GetDestructible();
            if (target != null && character_combat.CanAutoAttack(target))
            {
                Attack(target);
            }
            else
            {
                InteractWith(selectable, pos);
            }
        }

        //---- Navmesh ----

        public void CalculateNavmesh()
        {
            if (auto_move && use_navmesh && !calculating_path)
            {
                calculating_path = true;
                path_found = false;
                path_index = 0;
                auto_move_target_next = auto_move_target; //Default
                NavMeshTool.CalculatePath(transform.position, auto_move_target, 1 << 0, FinishCalculateNavmesh);
            }
        }

        private void FinishCalculateNavmesh(NavMeshToolPath path)
        {
            calculating_path = false;
            path_found = path.success;
            nav_paths = path.path;
            path_index = 0;
        }

        //---- Getters ----

        //Check if character is near an object of that group
        public bool IsNearGroup(GroupData group)
        {
            Selectable group_select = Selectable.GetNearestGroup(group, transform.position);
            return group_select != null && group_select.IsInUseRange(transform.position);
        }

        public ActionSleep GetSleepTarget()
        {
            return sleep_target;
        }

        public Destructible GetAutoAttackTarget()
        {
            return auto_move_attack;
        }

        public Selectable GetAutoSelectTarget()
        {
            return auto_move_select;
        }

        public GameObject GetAutoTarget()
        {
            GameObject auto_move_obj = null;
            if (auto_move_select != null && auto_move_select.type == SelectableType.Interact)
                auto_move_obj = auto_move_select.gameObject;
            if (auto_move_attack != null)
                auto_move_obj = auto_move_attack.gameObject;
            return auto_move_obj;
        }

        public InventoryData GetAutoDropInventory()
        {
            return auto_move_drop_inventory;
        }

        public Vector3 GetAutoMoveTarget()
        {
            return auto_move_target;
        }

        public bool IsDead()
        {
            return character_combat.IsDead();
        }

        public bool IsSleeping()
        {
            return is_sleep;
        }

        public bool IsFishing()
        {
            return is_fishing;
        }

        public bool IsRiding()
        {
            return character_ride != null && character_ride.IsRiding();
        }

        public bool IsSwimming()
        {
            return character_swim != null && character_swim.IsSwimming();
        }

        public bool IsClimbing()
        {
            return character_climb != null && character_climb.IsClimbing();
        }

        public bool IsDoingAction()
        {
            return is_action;
        }

        public bool IsJumping()
        {
            return character_jump != null && character_jump.IsJumping();
        }

        public bool IsAutoMove()
        {
            return auto_move;
        }

        public bool IsMoving()
        {
            if (IsRiding() && character_ride.GetAnimal() != null)
                return character_ride.GetAnimal().IsMoving();
            if (Climbing && Climbing.IsClimbing())
                return Climbing.IsMoving();

            Vector3 moveXZ = new Vector3(move.x, 0f, move.z);
            return moveXZ.magnitude > GetMoveSpeed() * moving_threshold;
        }

        public Vector3 GetMove()
        {
            return move;
        }

        public Vector3 GetFacing()
        {
            return facing;
        }

        public Vector3 GetMoveNormalized()
        {
            return move.normalized * Mathf.Clamp01(move.magnitude / GetMoveSpeed());
        }

        public float GetMoveSpeed()
        {
            float boost = 1f + character_attr.GetBonusEffectTotal(BonusType.SpeedBoost);
            float base_speed = IsSwimming() ? character_swim.swim_speed : move_speed;
            return base_speed * boost * character_attr.GetSpeedMult();
        }

        public Vector3 GetPosition()
        {
            if (IsRiding() && character_ride.GetAnimal() != null)
                return character_ride.GetAnimal().transform.position;
            return transform.position;
        }

        public Vector3 GetColliderCenter()
        {
            Vector3 scale = transform.lossyScale;
            return collide.transform.position + Vector3.Scale(collide.center, scale);
        }

        public float GetColliderHeightRadius()
        {
            Vector3 scale = transform.lossyScale;
            return collide.height * scale.y * 0.5f + ground_detect_dist; //radius is half the height minus offset
        }

        public float GetColliderRadius()
        {
            Vector3 scale = transform.lossyScale;
            return collide.radius * (scale.x + scale.y) * 0.5f;
        }

        public bool IsFronted()
        {
            return is_fronted;
        }

        public bool IsGrounded()
        {
            return is_grounded;
        }

        public bool IsControlsEnabled()
        {
            return controls_enabled && !TheUI.Get().IsFullPanelOpened();
        }

        public bool IsMovementEnabled()
        {
            return movement_enabled && !is_action && !IsRiding() && !IsClimbing();
        }

        public PlayerCharacterCombat Combat
        {
            get { return character_combat; }
        }

        public PlayerCharacterAttribute Attributes
        {
            get {return character_attr;}
        }

        public PlayerCharacterCraft Crafting
        {
            get { return character_craft; }
        }

        public PlayerCharacterInventory Inventory
        {
            get { return character_inventory; }
        }

        public PlayerCharacterJump Jumping
        {
            get { return character_jump; } //Can be null
        }

        public PlayerCharacterSwim Swimming
        {
            get { return character_swim; } //Can be null
        }

        public PlayerCharacterClimb Climbing
        {
            get { return character_climb; } //Can be null
        }

        public PlayerCharacterRide Riding
        {
            get { return character_ride; } //Can be null
        }

        public PlayerCharacterAnim Animation
        {
            get { return character_anim; } //Can be null
        }

        public PlayerCharacterData Data //Keep for compatibility with other versions, same than SaveData
        {
            get { return PlayerCharacterData.Get(player_id); }
        }

        public PlayerCharacterData SaveData
        {
            get { return PlayerCharacterData.Get(player_id); }
        }

        public InventoryData InventoryData
        {
            get { return character_inventory.InventoryData; }
        }

        public InventoryData EquipData
        {
            get { return character_inventory.EquipData; }
        }

        public static PlayerCharacter GetNearest(Vector3 pos, float range = 999f)
        {
            PlayerCharacter nearest = null;
            float min_dist = range;
            foreach (PlayerCharacter unit in players_list)
            {
                float dist = (unit.transform.position - pos).magnitude;
                if (dist < min_dist)
                {
                    min_dist = dist;
                    nearest = unit;
                }
            }
            return nearest;
        }

        public static PlayerCharacter GetFirst()
        {
            return player_first;
        }

        public static PlayerCharacter Get(int player_id=0)
        {
            foreach (PlayerCharacter player in players_list)
            {
                if (player.player_id == player_id)
                    return player;
            }
            return null;
        }

        public static List<PlayerCharacter> GetAll()
        {
            return players_list;
        }
    }

}