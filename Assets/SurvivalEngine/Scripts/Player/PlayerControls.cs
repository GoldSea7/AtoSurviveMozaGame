﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SurvivalEngine
{
    /// <summary>
    /// Keyboard controls manager
    /// </summary>

    public class PlayerControls : MonoBehaviour
    {
        public int player_id = 0;

        [Header("Actions")]
        public Key action_key = Key.Space;
        public Key attack_key = Key.LeftShift;
        public Key jump_key = Key.LeftCtrl;

        [Header("Camera")]
        public Key cam_rotate_left = Key.Q;
        public Key cam_rotate_right = Key.E;

        [Header("UI")]
        public Key craft_key = Key.C;
        public Key ui_select = Key.Enter;
        public Key ui_use = Key.RightShift;
        public Key ui_cancel = Key.Backspace;

        [Header("Menu")]
        public Key menu_accept = Key.Enter;
        public Key menu_cancel = Key.Backspace;
        public Key menu_pause = Key.Escape;

        [Header(" ---- Gamepad Mode ---- ")]
        public bool gamepad_controls = false; //In gamepad mode, anything that can usually be done with the mouse will be replaced by keyboard/gamepad controls, 
                                              //for example the build system will place building differently

        public delegate Vector2 MoveAction();
        public delegate bool PressAction();

        [HideInInspector]
        public bool gamepad_linked = false;
        public MoveAction gamepad_move;
        public MoveAction gamepad_freelook;
        public MoveAction gamepad_menu;
        public MoveAction gamepad_dpad;
        public MoveAction gamepad_camera; //Triggers
        public PressAction gamepad_pause; //Start
        public PressAction gamepad_action; //A
        public PressAction gamepad_attack; //X or R1
        public PressAction gamepad_jump; //Y
        public PressAction gamepad_craft; //L1
        public PressAction gamepad_use; //X
        public PressAction gamepad_accept; //A
        public PressAction gamepad_cancel; //B
        public System.Action gamepad_update;

        private Vector3 move;
        private Vector2 freelook;
        private Vector2 menu_move;
        private Vector2 ui_move;
        private bool menu_moved;
        private bool ui_moved;
        private float rotate_cam;

        private bool press_action;
        private bool press_attack;
        private bool press_jump;
        private bool press_craft;

        private bool press_accept;
        private bool press_cancel;
        private bool press_pause;
        private bool press_ui_select;
        private bool press_ui_use;
        private bool press_ui_cancel;

        private static PlayerControls control_first = null;
        private static List<PlayerControls> controls = new List<PlayerControls>();

        void Awake()
        {
            controls.Add(this);

            if (control_first == null || player_id < control_first.player_id)
                control_first = this;

            if (TheGame.IsMobile())
                gamepad_controls = false; //No gamepad on mobile
        }

        private void OnDestroy()
        {
            controls.Remove(this);
        }

        void Update()
        {
            move = Vector3.zero;
            freelook = Vector2.zero;
            menu_move = Vector2.zero;
            ui_move = Vector2.zero;
            rotate_cam = 0f;
            press_action = false;
            press_attack = false;
            press_jump = false;
            press_craft = false;

            press_accept = false;
            press_cancel = false;
            press_pause = false;
            press_ui_select = false;
            press_ui_use = false;
            press_ui_cancel = false;

            Vector2 wasd = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if(keyboard != null)
            {
                if (keyboard[Key.A].isPressed)
                    wasd += Vector2.left;
                if (keyboard[Key.D].isPressed)
                    wasd += Vector2.right;
                if (keyboard[Key.W].isPressed)
                    wasd += Vector2.up;
                if (keyboard[Key.S].isPressed)
                    wasd += Vector2.down;

                Vector2 arrows = Vector2.zero;
                if (keyboard[Key.LeftArrow].isPressed)
                    arrows += Vector2.left;
                if (keyboard[Key.RightArrow].isPressed)
                    arrows += Vector2.right;
                if (keyboard[Key.UpArrow].isPressed)
                    arrows += Vector2.up;
                if (keyboard[Key.DownArrow].isPressed)
                    arrows += Vector2.down;

                if (keyboard[cam_rotate_left].isPressed)
                    rotate_cam += -1f;
                if (keyboard[cam_rotate_right].isPressed)
                    rotate_cam += 1f;

                if (keyboard[action_key].wasPressedThisFrame)
                    press_action = true;
                if (keyboard[attack_key].wasPressedThisFrame)
                    press_attack = true;
                if (keyboard[jump_key].wasPressedThisFrame)
                    press_jump = true;
                if (keyboard[craft_key].wasPressedThisFrame)
                    press_craft = true;

                if (keyboard[menu_accept].wasPressedThisFrame)
                    press_accept = true;
                if (keyboard[menu_cancel].wasPressedThisFrame)
                    press_cancel = true;
                if (keyboard[menu_pause].wasPressedThisFrame)
                    press_pause = true;

                if (keyboard[ui_select].wasPressedThisFrame)
                    press_ui_select = true;
                if (keyboard[ui_use].wasPressedThisFrame)
                    press_ui_use = true;
                if (keyboard[ui_cancel].wasPressedThisFrame)
                    press_ui_cancel = true;

                Vector2 both = (arrows + wasd);
                move = new Vector3(wasd.x, 0f, wasd.y);
                if (gamepad_controls)
                    freelook = arrows;

                //Menu / UI
                if (!menu_moved && both.magnitude > 0.5f)
                {
                    menu_move = both;
                    menu_moved = true;
                }

                if (both.magnitude < 0.5f)
                    menu_moved = false;

                if (!ui_moved && arrows.magnitude > 0.5f)
                {
                    ui_move = arrows;
                    ui_moved = true;
                }

                if (arrows.magnitude < 0.5f)
                    ui_moved = false;
            }

            //Gamepad
            if (gamepad_linked && gamepad_controls) {

                Vector2 gmove = gamepad_move.Invoke();
                move += new Vector3(gmove.x, 0f, gmove.y);
                freelook += gamepad_freelook.Invoke();
                rotate_cam += gamepad_camera.Invoke().x;
                ui_move += gamepad_dpad.Invoke();
                menu_move += gamepad_menu.Invoke();
                menu_move += gamepad_dpad.Invoke();

                press_action = press_action || gamepad_action.Invoke();
                press_attack = press_attack || gamepad_attack.Invoke();
                press_jump = press_jump || gamepad_jump.Invoke();

                press_craft = press_craft || gamepad_craft.Invoke();
                press_accept = press_accept || gamepad_accept.Invoke();
                press_cancel = press_cancel || gamepad_cancel.Invoke();
                press_pause = press_pause || gamepad_pause.Invoke();
                press_ui_select = press_ui_select || gamepad_accept.Invoke();
                press_ui_use = press_ui_use || gamepad_use.Invoke();
                press_ui_cancel = press_ui_cancel || gamepad_cancel.Invoke();

                gamepad_update?.Invoke();
            }

            move = move.normalized * Mathf.Min(move.magnitude, 1f);
            freelook = freelook.normalized * Mathf.Min(freelook.magnitude, 1f);
        }

        public Vector3 GetMove() { return move; }
        public Vector2 GetFreelook() { return freelook; }
        public bool IsMoving() { return move.magnitude > 0.1f; }
        public float GetRotateCam() { return rotate_cam; }

        public bool IsPressAttack() { return press_attack; }
        public bool IsPressAction() { return press_action; }
        public bool IsPressJump() { return press_jump; }
        public bool IsPressCraft() { return press_craft; }

        public Vector2 GetUIMove() { return ui_move; }
        public Vector2 GetMenuMove() { return menu_move; }

        public bool IsPressMenuAccept() { return press_accept; }
        public bool IsPressMenuCancel() { return press_cancel; }
        public bool IsPressPause() { return press_pause; }
        public bool IsPressUISelect() { return press_ui_select; }
        public bool IsPressUIUse() { return press_ui_use; }
        public bool IsPressUICancel() { return press_ui_cancel; }

        public bool IsUIPressAny() { return ui_move.magnitude > 0.5f; }
        public bool IsUIPressLeft() { return ui_move.x < -0.5f; }
        public bool IsUIPressRight() { return ui_move.x > 0.5f; }
        public bool IsUIPressUp() { return ui_move.y > 0.5f; }
        public bool IsUIPressDown() { return ui_move.y < -0.5f; }

        public bool IsMenuPressLeft() { return menu_move.x < -0.5f; }
        public bool IsMenuPressRight() { return menu_move.x > 0.5f; }
        public bool IsMenuPressUp() { return menu_move.y > 0.5f; }
        public bool IsMenuPressDown() { return menu_move.y < -0.5f; }

        public bool IsGamePad()
        {
            return gamepad_controls;
        }

        public static bool IsAnyGamePad()
        {
            foreach (PlayerControls control in controls)
            {
                if (control.IsGamePad())
                    return true;
            }
            return false;
        }

        public static PlayerControls Get(int player_id = 0)
        {
            foreach (PlayerControls control in controls)
            {
                if (control.player_id == player_id)
                    return control;
            }
            return null;
        }

        public static PlayerControls GetFirst()
        {
            return control_first;
        }

        public static List<PlayerControls> GetAll()
        {
            return controls;
        }
    }

}