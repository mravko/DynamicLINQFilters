﻿using DynamicLinqFilters.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace DynamicLinqFilters.Extensions
{
    public static class LinqFilterExtensions
    {
        public static IEnumerable<T> Filter<T>(this IEnumerable<T> query, List<Filter> filters)
        {
            return Filter(query.AsQueryable(), filters);
        }

        public static IEnumerable<T> Filter<T>(this IEnumerable<T> query, FilterGroupsHolder filterGroupsHolder)
        {
            return Filter(query.AsQueryable(), filterGroupsHolder);
        }

        public static IQueryable<T> Filter<T>(this IQueryable<T> query, List<Filter> filters)
        {
            if (filters.Count == 0)
            {
                return query;
            }

            ParameterExpression pe = Expression.Parameter(typeof(T), "x");

            var expression = CreateFilterExpression<T>(filters, pe);

            MethodCallExpression whereCallExpression = Expression.Call(
                typeof(Queryable),
                "Where",
                new Type[] { query.ElementType },
                query.Expression,
                Expression.Lambda<Func<T, bool>>(expression,
                new ParameterExpression[] { pe }));

            return query.Provider.CreateQuery<T>(whereCallExpression);
        }

        public static IQueryable<T> Filter<T>(this IQueryable<T> query, FilterGroupsHolder filterGroupsHolder)
        {
            if (filterGroupsHolder.FilterGroups.Count == 0)
            {
                return query;
            }

            Expression exp = null;
            ParameterExpression pe = Expression.Parameter(typeof(T), "x");

            foreach (var filterGroup in filterGroupsHolder.FilterGroups)
            {
                var filters = filterGroup.Filters;

                if (filters.Count == 0)
                {
                    continue;
                }

                var expression = CreateFilterExpression<T>(filters, pe, filterGroup.FilterGroupJoinType);

                if (exp == null)
                {
                    exp = expression;
                }
                else
                {
                    exp = ExpressionHelpers.ConcatExpressionsWithOperator(exp, expression, filterGroupsHolder.FilterGroupJoinType);
                }
            }

            MethodCallExpression whereCallExpression = Expression.Call(
               typeof(Queryable),
               "Where",
               new Type[] { query.ElementType },
               query.Expression,
               Expression.Lambda<Func<T, bool>>(exp, new ParameterExpression[] { pe }));

            return query.Provider.CreateQuery<T>(whereCallExpression);
        }

        private static Expression CreateFilterExpression<T>(List<Filter> filters, ParameterExpression pe, FilterJoinType filterGroupJoinType = FilterJoinType.And)
        {
            Expression exp = null;
            foreach (var filter in filters)
            {
                Expression left = ExpressionHelpers.BuildNestedPropertyExpression(pe, filter.PropertyName);
                Expression builded = BuildFilterValuesExpression(left, filter.Value, filter.FilterValueJoinType);

                if (exp == null)
                {
                    exp = builded;
                }
                else
                {
                    exp = ExpressionHelpers.ConcatExpressionsWithOperator(exp, builded, filterGroupJoinType);
                }
            }

            return exp;
        }

        private static bool IsNullable(Type type) => Nullable.GetUnderlyingType(type) != null;

        private static IList ConvertList(Type newItemType, IList source)
        {
            var listType = typeof(List<>);
            Type[] typeArgs = { newItemType };
            var genericListType = listType.MakeGenericType(typeArgs);
            var typedList = (IList)Activator.CreateInstance(genericListType);
            foreach (var item in source)
            {
                typedList.Add(Convert.ChangeType(item, newItemType));
            }
            return typedList;
        }

        private static Expression BuildFilterValuesExpression(Expression left, List<FilterValue> filterValues, FilterJoinType filterValueJoinType = FilterJoinType.Or)
        {
            Expression exp = null;

            foreach (var filterValue in filterValues)
            {
                Expression right = null;
                if (IsNullable(left.Type))
                {
                    var underlyingType = Nullable.GetUnderlyingType(left.Type);
                    Type type = typeof(Nullable<>).MakeGenericType(underlyingType);
                    right = Expression.Convert(Expression.Constant(Convert.ChangeType(filterValue.Value, underlyingType)), type);
                }
                else if (filterValue.Value.GetType().IsGenericType && filterValue.Value is IList)
                {
                    IList list = ConvertList(left.Type, filterValue.Value as IList);
                    right = Expression.Constant(list);
                }
                else
                {
                    var value = Convert.ChangeType(filterValue.Value, left.Type);
                    right = Expression.Constant(value);
                }

                Expression concatenated = ExpressionHelpers.ConcatExpressionsWithOperator(left, right, filterValue.Operator);
                if (exp == null)
                {
                    exp = concatenated;
                }
                else
                {
                    exp = ExpressionHelpers.ConcatExpressionsWithOperator(exp, concatenated, filterValueJoinType);
                }
            }

            return exp;
        }
    }
}