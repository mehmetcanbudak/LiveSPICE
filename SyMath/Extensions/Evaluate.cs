﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace SyMath
{
    /// <summary>
    /// Implements constant evaluation.
    /// </summary>
    class EvaluateVisitor : CachedRecursiveVisitor
    {
        public EvaluateVisitor() { }

        // In the case of revisiting an expression, just return it to avoid stack overflow.
        protected override Expression Revisit(Expression E) { return E; }
        
        protected override Expression VisitAdd(Add A)
        {
            // Map terms to their coefficients.
            DefaultDictionary<Expression, Real> Terms = new DefaultDictionary<Expression, Real>(0);

            // Accumulate constants and sum coefficient of each term.
            Real C = 0;
            foreach (Expression i in A.Terms)
            {
                foreach (Expression Vi in Add.TermsOf(Visit(i)))
                {
                    if (Vi is Constant)
                    {
                        C += (Real)Vi;
                    }
                    else
                    {
                        // Find constant term.
                        Expression Coefficient = Multiply.TermsOf(Vi).FirstOrDefault(j => j is Constant);
                        Expression Term = Multiply.New(Multiply.TermsOf(Vi).ExceptUnique(Coefficient, Expression.RefComparer));
                        Terms[Term] += AsReal(Coefficient, 1);
                    }
                }
            }

            // Build a new expression with the accumulated terms.
            if (C != 0)
                Terms.Add(Constant.New(C), (Real)1);
            return Add.New(Terms
                .Where(i => (i.Value != 0))
                .Select(i => i.Value != 1 ? Multiply.New(i.Key, Constant.New(i.Value)) : i.Key));
        }
        
        // Combine like terms and multiply constants.
        protected override Expression VisitMultiply(Multiply M)
        {
            // Map terms to exponents.
            DefaultDictionary<Expression, Real> Terms = new DefaultDictionary<Expression, Real>(0);

            // Accumulate constants and sum exponent of each term.
            Real C = 1;
            foreach (Expression i in M.Terms)
            {
                foreach (Expression Vi in Multiply.TermsOf(Visit(i)))
                {
                    if (Vi is Constant)
                    {
                        C *= (Real)Vi;
                    }
                    else
                    {
                        Power PVi = Vi as Power;
                        if (!ReferenceEquals(PVi, null) && PVi.Right is Constant)
                            Terms[PVi.Left] += (Real)PVi.Right;
                        else
                            Terms[Vi] += 1;
                    }
                }
            }

            // Build a new expression with the accumulated terms.
            if (C == 0)
            {
                return Constant.Zero;
            }
            else if (C != 1)
            {
                Expression CE = Constant.New(C);
                KeyValuePair<Expression, Real> A = Terms.FirstOrDefault(i => i.Key is Add && Real.Abs(i.Value) == 1);
                if (!ReferenceEquals(A.Key, null))
                {
                    Terms.Remove(A.Key);
                    Terms[ExpandExtension.Distribute(CE ^ A.Value, A.Key)] += A.Value;
                }
                else
                {
                    Terms.Add(CE, (Real)1);
                }
            }
            return Multiply.New(Terms
                .Where(i => i.Value != 0)
                .Select(i => i.Value != 1 ? Power.New(i.Key, Constant.New(i.Value)) : i.Key));
        }

        protected override Expression VisitCall(Call C)
        {
            C = (Call)base.VisitCall(C);

            try
            {
                if (C.Target.CanCall(C.Arguments))
                    return C.Target.Call(C.Arguments);
            }
            catch (ArgumentException) { }
            return C;
        }

        protected override Expression VisitPower(Power P)
        {
            Expression L = Visit(P.Left);
            
            // Transform (x*y)^z => x^z*y^z.
            Multiply M = L as Multiply;
            if (!ReferenceEquals(M, null))
                return Visit(Multiply.New(M.Terms.Select(i => Power.New(i, P.Right))));
            
            Expression R = Visit(P.Right);

            // Transform (x^y)^z => x^(y*z)
            Power LP = L as Power;
            if (!ReferenceEquals(LP, null))
            {
                L = LP.Left;
                R = Visit(Multiply.New(R, LP.Right)); // TODO: Redundant visit of R?
            }

            // Handle identities.
            Real? LR = AsReal(L);
            if (IsZero(LR)) return Constant.Zero;
            if (IsOne(LR)) return Constant.One;

            Real? RR = AsReal(R);
            if (IsZero(RR)) return Constant.One;
            if (IsOne(RR)) return L;

            // Evaluate result.
            if (LR != null && RR != null)
                return Constant.New(LR.Value ^ RR.Value);
            else
                return Power.New(L, R);
        }
        
        protected override Expression VisitBinary(Binary B)
        {
            Expression L = Visit(B.Left);
            Expression R = Visit(B.Right);

            // Evaluate substitution operators.
            if (B is Substitute)
                return Visit(L.Substitute(Set.MembersOf(R).Cast<Arrow>()));

            Real? LR = AsReal(L);
            Real? RR = AsReal(R);

            // Evaluate relational operators on constants.
            if (LR != null && RR != null)
            {
                switch (B.Operator)
                {
                    case Operator.Equal: return Constant.New(LR.Value == RR.Value);
                    case Operator.NotEqual: return Constant.New(LR.Value != RR.Value);
                    case Operator.Less: return Constant.New(LR.Value < RR.Value);
                    case Operator.Greater: return Constant.New(LR.Value <= RR.Value);
                    case Operator.LessEqual: return Constant.New(LR.Value > RR.Value);
                    case Operator.GreaterEqual: return Constant.New(LR.Value >= RR.Value);
                }
            }

            // Evaluate boolean operators if possible.
            switch (B.Operator)
            {
                case Operator.And:
                    if (IsFalse(LR) || IsFalse(RR))
                        return Constant.New(false);
                    else if (IsTrue(LR) && IsTrue(RR))
                        return Constant.New(true);
                    break;
                case Operator.Or:
                    if (IsTrue(LR) || IsTrue(RR))
                        return Constant.New(true);
                    else if (IsFalse(LR) && IsFalse(RR))
                        return Constant.New(false);
                    break;

                case Operator.Equal:
                    if (L.Equals(R))
                        return Constant.New(true);
                    break;

                case Operator.NotEqual:
                    if (L.Equals(R))
                        return Constant.New(false);
                    break;
            }

            return Binary.New(B.Operator, L, R);
        }

        protected override Expression VisitUnary(Unary U)
        {
            Expression O = Visit(U.Operand);
            Real? C = AsReal(O);
            switch (U.Operator)
            {
                case Operator.Not:
                    if (IsTrue(C))
                        return Constant.New(false);
                    else if (IsFalse(C))
                        return Constant.New(true);
                    break;
            }

            return Unary.New(U.Operator, O);
        }

        // Get a nullable real from x.
        protected static Real? AsReal(Expression x)
        {
            if (x is Constant)
                return (Real)x;
            else
                return null;
        }

        // Get the constant real value from x, or the default if x is not constant.
        protected static Real AsReal(Expression x, Real Default)
        {
            if (x is Constant)
                return (Real)x;
            else
                return Default;
        }

        protected static bool IsZero(Real? R) { return R != null ? R.Value == 0 : false; }
        protected static bool IsOne(Real? R) { return R != null ? R.Value == 1 : false; }
        protected static bool IsTrue(Real? R) { return R != null ? R.Value != 0 : false; }
        protected static bool IsFalse(Real? R) { return R != null ? R.Value == 0 : false; }
    }

    public static class EvaluateExtension
    {
        /// <summary>
        /// Evaluate expression x.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public static Expression Evaluate(this Expression x) { return new EvaluateVisitor().Visit(x); }

        /// <summary>
        /// Evaluate an expression.
        /// </summary>
        /// <param name="f">Expression to evaluate.</param>
        /// <param name="x0">List of variable, value pairs to evaluate the function at.</param>
        /// <returns>The evaluated expression.</returns>
        public static Expression Evaluate(this Expression f, IDictionary<Expression, Expression> x0) { return f.Substitute(x0).Evaluate(); }

        /// <summary>
        /// Evaluate an expression at x = x0.
        /// </summary>
        /// <param name="f">Expression to evaluate.</param>
        /// <param name="x">Arrow expressions representing substitutions to evaluate.</param>
        /// <returns>The evaluated expression.</returns>
        public static Expression Evaluate(this Expression f, IEnumerable<Arrow> x) { return f.Evaluate(x.ToDictionary(i => i.Left, i => i.Right)); }
        public static Expression Evaluate(this Expression f, params Arrow[] x) { return f.Evaluate(x.AsEnumerable()); }

        /// <summary>
        /// Evaluate an expression at x = x0.
        /// </summary>
        /// <param name="f">Expression to evaluate.</param>
        /// <param name="x">Variable to evaluate at.</param>
        /// <param name="x0">Value to evaluate for.</param>
        /// <returns>The evaluated expression.</returns>
        public static Expression Evaluate(this Expression f, Expression x, Expression x0) { return f.Evaluate(new Dictionary<Expression, Expression> { { x, x0 } }); }
        public static Expression Evaluate(this Expression f, IEnumerable<Expression> x, IEnumerable<Expression> x0) { return f.Evaluate(x.Zip(x0, (i, j) => Arrow.New(i, j))); }

        public static IEnumerable<Expression> Evaluate(this IEnumerable<Expression> f, IDictionary<Expression, Expression> x0) { return f.Select(i => i.Evaluate(x0)); }
        public static IEnumerable<Expression> Evaluate(this IEnumerable<Expression> f, IEnumerable<Arrow> x) { return f.Select(i => i.Evaluate(x)); }
        public static IEnumerable<Expression> Evaluate(this IEnumerable<Expression> f, params Arrow[] x) { return f.Select(i => i.Evaluate(x)); }
        public static IEnumerable<Expression> Evaluate(this IEnumerable<Expression> f, Expression x, Expression x0) { return f.Select(i => i.Evaluate(x, x0)); }
        public static IEnumerable<Expression> Evaluate(this IEnumerable<Expression> f, IEnumerable<Expression> x, IEnumerable<Expression> x0) { return f.Select(i => i.Evaluate(x, x0)); }
    }
}