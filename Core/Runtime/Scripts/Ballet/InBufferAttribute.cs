using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace Oxipital
{
    public class InBuffer : Attribute
    {
        public int index;
        public bool useCustomFunc;

        public InBuffer(int index, bool useCustomFunc = false)
        {
            this.index = index;
            this.useCustomFunc = useCustomFunc;
        }
    }

    // Layout of the [InBuffer] fields of a group type, analyzed once per type.
    // Write copies the field values into the group's float buffer through compiled getters: no reflection and
    // no allocation per frame (reading the attributes and boxing the values every frame cost about 1.4 ms and
    // 60 KB of garbage per frame for 8 groups).
    // Same rules as before: a later field with the same index replaces the earlier one, Vector3 / Color take
    // 3 slots and Vector4 4, bool / int / double / enum are converted to float, other types are not written.
    internal sealed class InBufferLayout
    {
        enum Kind { None, Float, Vector3, Vector4, Color }

        sealed class Field
        {
            public int index;
            public Kind kind;
            public bool custom;
            public FieldInfo info;
            public Func<object, float> getFloat;
            public Func<object, Vector3> getVector3;
            public Func<object, Vector4> getVector4;
            public Func<object, Color> getColor;

            public bool HasGetter => getFloat != null || getVector3 != null || getVector4 != null || getColor != null;
        }

        static readonly Dictionary<Type, InBufferLayout> layouts = new Dictionary<Type, InBufferLayout>();

        readonly Field[] fields;

        // Number of floats of the group data (end of the last field)
        public int GroupFloatCount { get; }

        public static InBufferLayout Get(Type type)
        {
            if (!layouts.TryGetValue(type, out InBufferLayout layout))
            {
                layout = new InBufferLayout(type);
                layouts[type] = layout;
            }
            return layout;
        }

        InBufferLayout(Type type)
        {
            var byIndex = new Dictionary<int, Field>();
            int end = 0;
            bool allCompiled = true;

            foreach (FieldInfo info in type.GetFields())
            {
                InBuffer attribute = info.GetCustomAttribute<InBuffer>();
                if (attribute == null) continue;

                Type fieldType = info.FieldType;
                int floats = fieldType == typeof(Vector3) || fieldType == typeof(Color) ? 3 : fieldType == typeof(Vector4) ? 4 : 1;
                end = Math.Max(end, attribute.index + floats);

                var field = new Field { index = attribute.index, kind = KindOf(fieldType), custom = attribute.useCustomFunc, info = info };
                if (!field.custom && field.kind != Kind.None)
                {
                    try { CompileGetter(field); }
                    catch (Exception) { allCompiled = false; }
                }
                byIndex[attribute.index] = field;
            }

            fields = byIndex.Values.ToArray();
            GroupFloatCount = end;
            if (!allCompiled) Debug.LogWarning($"InBuffer : the getters of {type.Name} could not be compiled, its buffer is filled by reflection (slower)");
        }

        // offset : position of the group data in the buffer (after the 2-float header)
        public void Write(object group, float[] data, int offset, Func<int, object> customValue)
        {
            foreach (Field field in fields)
            {
                int i = field.index + offset;

                if (field.custom || field.kind != Kind.None && !field.HasGetter)
                {
                    WriteBoxed(field.info.FieldType, field.custom ? customValue(i) : field.info.GetValue(group), data, i);
                    continue;
                }

                switch (field.kind)
                {
                    case Kind.Float:
                        data[i] = field.getFloat(group);
                        break;
                    case Kind.Vector3:
                        Vector3 v3 = field.getVector3(group);
                        data[i] = v3.x;
                        data[i + 1] = v3.y;
                        data[i + 2] = v3.z;
                        break;
                    case Kind.Vector4:
                        Vector4 v4 = field.getVector4(group);
                        data[i] = v4.x;
                        data[i + 1] = v4.y;
                        data[i + 2] = v4.z;
                        data[i + 3] = v4.w;
                        break;
                    case Kind.Color:
                        Color c = field.getColor(group);
                        data[i] = c.r;
                        data[i + 1] = c.g;
                        data[i + 2] = c.b;
                        break;
                }
            }
        }

        static Kind KindOf(Type type)
        {
            if (type == typeof(Vector3)) return Kind.Vector3;
            if (type == typeof(Vector4)) return Kind.Vector4;
            if (type == typeof(Color)) return Kind.Color;
            if (type == typeof(float) || type == typeof(bool) || type == typeof(int) || type == typeof(double) || type.BaseType == typeof(Enum)) return Kind.Float;
            return Kind.None;
        }

        static void CompileGetter(Field field)
        {
            ParameterExpression group = Expression.Parameter(typeof(object), "group");
            Expression value = Expression.Field(Expression.Convert(group, field.info.DeclaringType), field.info);

            switch (field.kind)
            {
                case Kind.Float:
                    field.getFloat = Expression.Lambda<Func<object, float>>(ToFloat(value), group).Compile();
                    break;
                case Kind.Vector3:
                    field.getVector3 = Expression.Lambda<Func<object, Vector3>>(value, group).Compile();
                    break;
                case Kind.Vector4:
                    field.getVector4 = Expression.Lambda<Func<object, Vector4>>(value, group).Compile();
                    break;
                case Kind.Color:
                    field.getColor = Expression.Lambda<Func<object, Color>>(value, group).Compile();
                    break;
            }
        }

        static Expression ToFloat(Expression value)
        {
            Type type = value.Type;
            if (type == typeof(float)) return value;
            if (type == typeof(bool)) return Expression.Condition(value, Expression.Constant(1f), Expression.Constant(0f));
            if (type.IsEnum) return Expression.Convert(Expression.Convert(value, typeof(int)), typeof(float));
            return Expression.Convert(value, typeof(float));
        }

        // Former per-frame path, kept for useCustomFunc fields and when the getters cannot be compiled
        static void WriteBoxed(Type type, object value, float[] data, int i)
        {
            if (type == typeof(Vector3))
            {
                Vector3 v = (Vector3)value;
                data[i] = v.x;
                data[i + 1] = v.y;
                data[i + 2] = v.z;
            }
            else if (type == typeof(Vector4))
            {
                Vector4 v = (Vector4)value;
                data[i] = v.x;
                data[i + 1] = v.y;
                data[i + 2] = v.z;
                data[i + 3] = v.w;
            }
            else if (type == typeof(Color))
            {
                Color v = (Color)value;
                data[i] = v.r;
                data[i + 1] = v.g;
                data[i + 2] = v.b;
            }
            else if (type == typeof(float)) data[i] = (float)value;
            else if (type == typeof(bool)) data[i] = (bool)value ? 1 : 0;
            else if (type == typeof(int)) data[i] = (int)value;
            else if (type == typeof(double)) data[i] = (float)(double)value;
            else if (type.BaseType == typeof(Enum)) data[i] = (float)(int)value;
        }
    }
}
