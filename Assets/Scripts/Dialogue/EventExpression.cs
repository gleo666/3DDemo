using System;
using System.Globalization;
using UnityEngine;

namespace DialogueSystem
{
    /// <summary>
    /// 解析并执行事件里的条件表达式和赋值表达式。
    /// 支持：
    ///   条件：Player.HasKeyCard == true && NPC_Engineer.Trust >= 10 || Self.IsAngry
    ///   赋值：Player.DoorOpen = true
    /// && 优先级高于 ||，不支持括号。
    /// </summary>
    public static class EventExpression
    {
        private const float Epsilon = 0.0001f;

        public static bool EvaluateCondition(string expression, Interactable speaker, string context)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                Warn(context, expression, "条件为空");
                return false;
            }

            // || 优先级最低，先按 || 切分
            string[] orParts = expression.Split(new[] { "||" }, StringSplitOptions.None);
            for (int i = 0; i < orParts.Length; i++)
            {
                if (EvaluateAnd(orParts[i], speaker, context)) return true;
            }

            return false;
        }

        public static bool ApplyAssignment(string assignment, Interactable speaker, string context)
        {
            if (string.IsNullOrWhiteSpace(assignment))
            {
                Warn(context, assignment, "赋值表达式为空");
                return false;
            }

            if (!TrySplitAssignment(assignment, out string left, out string right, out string error))
            {
                Warn(context, assignment, error);
                return false;
            }

            if (!TryParseLValue(left, out string target, out string variable, out error))
            {
                Warn(context, assignment, error);
                return false;
            }

            if (!TryResolveHolder(target, speaker, out EventVariableHolder holder, out error))
            {
                Warn(context, assignment, error);
                return false;
            }

            if (!holder.TryGetVariable(variable, out EventVariable v))
            {
                Warn(context, assignment, "找不到变量 " + variable + "（Holder: " + holder.OwnerId + "）");
                return false;
            }

            if (v.Type == EventVariableType.Bool)
            {
                if (!TryParseBoolLiteral(right, out bool boolValue))
                {
                    Warn(context, assignment, "Bool 变量只能赋 true/false/1/0");
                    return false;
                }

                v.BoolValue = boolValue;
                return true;
            }

            if (!TryParseNumber(right, out float numberValue))
            {
                Warn(context, assignment, "Number 变量只能赋数字");
                return false;
            }

            v.NumberValue = numberValue;
            return true;
        }

        private static bool EvaluateAnd(string expression, Interactable speaker, string context)
        {
            string[] andParts = expression.Split(new[] { "&&" }, StringSplitOptions.None);
            for (int i = 0; i < andParts.Length; i++)
            {
                if (!EvaluateComparison(andParts[i], speaker, context)) return false;
            }

            return true;
        }

        private static bool EvaluateComparison(string text, Interactable speaker, string context)
        {
            text = text.Trim();
            if (text.Length == 0)
            {
                Warn(context, text, "条件片段为空");
                return false;
            }

            if (!TryFindOperator(text, out int operatorIndex, out string op))
            {
                Warn(context, text, "出现了单个 = 或 !，比较请用 ==、!=");
                return false;
            }

            string left = text;
            string right = null;

            if (op != null)
            {
                left = text.Substring(0, operatorIndex).Trim();
                right = text.Substring(operatorIndex + op.Length).Trim();

                if (left.Length == 0 || right.Length == 0)
                {
                    Warn(context, text, "运算符两侧不能为空");
                    return false;
                }
            }

            if (!TryParseLValue(left, out string target, out string variable, out string error))
            {
                Warn(context, text, error);
                return false;
            }

            if (!TryResolveHolder(target, speaker, out EventVariableHolder holder, out error))
            {
                Warn(context, text, error);
                return false;
            }

            if (!holder.TryGetVariable(variable, out EventVariable v))
            {
                Warn(context, text, "找不到变量 " + variable + "（Holder: " + holder.OwnerId + "）");
                return false;
            }

            if (v.Type == EventVariableType.Bool)
                return EvaluateBool(v, op, right, context, text);

            return EvaluateNumber(v, op, right, context, text);
        }

        private static bool EvaluateBool(EventVariable v, string op, string right, string context, string expression)
        {
            if (op == null) return v.BoolValue;

            if (op != "==" && op != "!=")
            {
                Warn(context, expression, "Bool 变量只支持 == 和 !=");
                return false;
            }

            if (!TryParseBoolLiteral(right, out bool expected))
            {
                Warn(context, expression, "Bool 变量的值只能是 true/false/1/0");
                return false;
            }

            return op == "==" ? v.BoolValue == expected : v.BoolValue != expected;
        }

        private static bool EvaluateNumber(EventVariable v, string op, string right, string context, string expression)
        {
            if (op == null)
            {
                Warn(context, expression, "数字变量必须写比较运算符");
                return false;
            }

            if (!TryParseNumber(right, out float expected))
            {
                Warn(context, expression, "数字格式不正确：" + right);
                return false;
            }

            float actual = v.NumberValue;

            switch (op)
            {
                case "==": return Mathf.Abs(actual - expected) < Epsilon;
                case "!=": return Mathf.Abs(actual - expected) >= Epsilon;
                case ">":  return actual > expected;
                case ">=": return actual >= expected;
                case "<":  return actual < expected;
                case "<=": return actual <= expected;
                default:
                    Warn(context, expression, "不支持的数字运算符：" + op);
                    return false;
            }
        }

        /// <summary>
        /// 返回 false 表示表达式里出现了单个 = 或 !；返回 true 时 op 为 null 表示没有运算符。
        /// </summary>
        private static bool TryFindOperator(string text, out int index, out string op)
        {
            index = -1;
            op = null;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '=')
                {
                    if (i + 1 < text.Length && text[i + 1] == '=')
                    {
                        index = i;
                        op = "==";
                        return true;
                    }

                    return false;
                }

                if (c == '!')
                {
                    if (i + 1 < text.Length && text[i + 1] == '=')
                    {
                        index = i;
                        op = "!=";
                        return true;
                    }

                    return false;
                }

                if (c == '>')
                {
                    if (i + 1 < text.Length && text[i + 1] == '=')
                    {
                        index = i;
                        op = ">=";
                        return true;
                    }

                    index = i;
                    op = ">";
                    return true;
                }

                if (c == '<')
                {
                    if (i + 1 < text.Length && text[i + 1] == '=')
                    {
                        index = i;
                        op = "<=";
                        return true;
                    }

                    index = i;
                    op = "<";
                    return true;
                }
            }

            return true;
        }

        private static bool TryParseLValue(string text, out string target, out string variable, out string error)
        {
            target = null;
            variable = null;
            error = null;

            text = text.Trim();
            if (text.Length == 0)
            {
                error = "左值不能为空";
                return false;
            }

            int dot = text.LastIndexOf('.');
            if (dot >= 0)
            {
                target = text.Substring(0, dot).Trim();
                variable = text.Substring(dot + 1).Trim();
            }
            else
            {
                target = "Self";
                variable = text;
            }

            if (target.Length == 0)
            {
                error = "对象名为空";
                return false;
            }

            if (variable.Length == 0)
            {
                error = "变量名为空";
                return false;
            }

            if (ContainsWhitespace(target) || ContainsOperatorChar(target))
            {
                error = "对象 ID 包含非法字符：" + target;
                return false;
            }

            if (ContainsWhitespace(variable) || ContainsOperatorChar(variable))
            {
                error = "变量名包含非法字符：" + variable;
                return false;
            }

            return true;
        }

        private static bool TryResolveHolder(string target, Interactable speaker, out EventVariableHolder holder, out string error)
        {
            holder = null;
            error = null;

            if (string.Equals(target, "Self", StringComparison.OrdinalIgnoreCase))
            {
                if (speaker == null)
                {
                    error = "Self 无法解析：当前没有对话对象";
                    return false;
                }

                holder = speaker.GetComponentInParent<EventVariableHolder>();
                if (holder == null)
                {
                    error = "对话对象 " + speaker.name + " 上没有 EventVariableHolder";
                    return false;
                }

                return true;
            }

            if (!EventVariableRegistry.TryGet(target, out holder))
            {
                error = "找不到 OwnerId 为 " + target + " 的 EventVariableHolder";
                return false;
            }

            return true;
        }

        private static bool TrySplitAssignment(string text, out string left, out string right, out string error)
        {
            left = null;
            right = null;
            error = null;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '=') continue;

                if (i + 1 < text.Length && text[i + 1] == '=')
                {
                    error = "赋值要用单个 =，== 是比较";
                    return false;
                }

                if (i > 0 &&
                    (text[i - 1] == '=' || text[i - 1] == '!' || text[i - 1] == '<' || text[i - 1] == '>'))
                {
                    error = "赋值要用单个 =";
                    return false;
                }

                left = text.Substring(0, i).Trim();
                right = text.Substring(i + 1).Trim();

                if (left.Length == 0 || right.Length == 0)
                {
                    error = "赋值号两侧不能为空";
                    return false;
                }

                return true;
            }

            error = "缺少赋值号 =";
            return false;
        }

        private static bool TryParseBoolLiteral(string text, out bool value)
        {
            value = false;
            if (string.IsNullOrEmpty(text)) return false;

            text = text.Trim();

            if (text.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
            {
                if (Mathf.Abs(number - 1f) < Epsilon)
                {
                    value = true;
                    return true;
                }

                if (Mathf.Abs(number) < Epsilon)
                {
                    value = false;
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseNumber(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool ContainsWhitespace(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i])) return true;
            }

            return false;
        }

        private static bool ContainsOperatorChar(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '&' || c == '|' || c == '=' || c == '<' || c == '>' || c == '!') return true;
            }

            return false;
        }

        private static void Warn(string context, string expression, string reason)
        {
            Debug.LogWarning("[Dialogue] 事件 '" + context + "' 的表达式 \"" + expression + "\" 无法处理：" + reason);
        }
    }
}
