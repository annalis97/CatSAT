﻿#region Copyright
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Solution.cs" company="Ian Horswill">
// Copyright (C) 2018 Ian Horswill
//  
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in the
// Software without restriction, including without limitation the rights to use, copy,
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
// and to permit persons to whom the Software is furnished to do so, subject to the
// following conditions:
//  
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
#endregion
#define RANDOMIZE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace PicoSAT
{
    /// <summary>
    /// The output of a program; a model satisfying the clauses of the Problem.
    /// Note: for packaging reasons, this is also where the actual solver code lives,
    /// rather than in Program.
    /// </summary>
    [DebuggerDisplay("{" + nameof(Model) + "}")]
    public class Solution
    {
        #region Solver parameters
        /// <summary>
        /// Number of flips of propositions we can try before we give up and start over.
        /// </summary>
        public readonly int MaxFlips;
        /// <summary>
        /// Number of times we can start over before we give up entirely.
        /// </summary>
        public readonly int MaxTries;

        /// <summary>
        /// Probability that the solver will flip a random variable rather than a variable from an unsatisfied clause.
        /// </summary>
        public readonly int RandomFlipProbability;
        #endregion

        #region Solver state
        /// <summary>
        /// The Program for which this is a solution.
        /// </summary>
        public readonly Problem Problem;

        /// <summary>
        /// States of the different propositions of the Program, indexed by proposition number.
        /// </summary>
        private readonly bool[] propositions;

        /// <summary>
        /// Number of presently true disjuncts in each of the Program's clauses, index by clause number.
        /// </summary>
        private readonly ushort[] trueDisjunctCount;

        /// <summary>
        /// Total number of unsatisfied clauses
        /// </summary>
        private readonly List<ushort> unsatisfiedClauses = new List<ushort>();
        #endregion

        internal Solution(Problem problem, int maxFlips, int maxTries, int randomFlipProbability)
        {
            Problem = problem;
            MaxFlips = maxFlips;
            MaxTries = maxTries;
            RandomFlipProbability = randomFlipProbability;
            propositions = new bool[problem.Variables.Count];
            trueDisjunctCount = new ushort[problem.Clauses.Count];
        }

        public string Model
        {
            // ReSharper disable once UnusedMember.Local
            get
            {
                var b = new StringBuilder();
                var firstOne = true;
                b.Append("<");
                for (int i = 1; i < propositions.Length; i++)
                {
                    if (propositions[i] && !Problem.Variables[i].Proposition.IsInternal)
                    {
                        if (firstOne)
                            firstOne = false;
                        else
                            b.Append(", ");
                        b.Append(Problem.Variables[i].Proposition);
                    }
                }
                b.Append(">");
                return b.ToString();
            }
        }

        #region Checking truth values
        /// <summary>
        /// Test the truth of the specified literal within the model
        /// </summary>
        // ReSharper disable once UnusedMember.Global
        public bool this[Literal l] => IsTrue(l);

        /// <summary>
        /// Test the truth of the specified literal within the model
        /// </summary>
        public bool this[Proposition p] => IsTrue(p);

        /// <summary>
        /// Test the truth of a literal (positive or negative) in the model.
        /// </summary>
        /// <param name="literal">Index of the literal (negative value = negative literal)</param>
        /// <returns>True if the literal is true in the model</returns>
        public bool IsTrue(short literal)
        {
            Debug.Assert(literal != 0, "0 is not a valid literal value!");
            if (literal > 0)
                return propositions[literal];
            return !propositions[-literal];
        }

        /// <summary>
        /// Test the truth of a proposition/positive literal
        /// </summary>
        /// <param name="index">Index of the proposition</param>
        /// <returns>True if the proposition is true in the model</returns>
        public bool IsTrue(ushort index)
        {
            return propositions[index];
        }

        /// <summary>
        /// Test the truth of the specified proposition within the model
        /// </summary>
        public bool IsTrue(Proposition p)
        {
            return IsTrue(p.Index);
        }

        /// <summary>
        /// Test the truth of the specified literal within the model
        /// </summary>
        public bool IsTrue(Literal l)
        {
            switch (l)
            {
                case Proposition p:
                    return IsTrue(p);

                case Negation n:
                    return !IsTrue(n.Proposition);

                default:
                    throw new ArgumentException($"Internal error - invalid literal {l}");
            }
        }
        #endregion

        #region Quantifiers
        public bool Quantify(int min, int max, IEnumerable<Literal> literals)
        {
            var enumerable = literals as Literal[] ?? literals.ToArray();
            if (max == 0)
            {
                max = enumerable.Length;
            }
            var c = Count(enumerable);
            return c >= min && c <= max;
        }

        public int Count(IEnumerable<Literal> literals)
        {
            return literals.Count(IsTrue);
        }

        public bool All(IEnumerable<Literal> literals)
        {
            var lits = literals.ToArray();
            return Quantify(lits.Length, lits.Length, lits);
        }

        // ReSharper disable once UnusedMember.Global
        public bool Exists(IEnumerable<Literal> literals)
        {
            return literals.Any(IsTrue);
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public bool Unique(IEnumerable<Literal> literals)
        {
            return Quantify(1, 1, literals);
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public bool Exactly(int n, IEnumerable<Literal> literals)
        {
            return Quantify(n, n, literals);
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public bool AtMost(int n, IEnumerable<Literal> literals)
        {
            return Quantify(0, n, literals);
        }

        // ReSharper disable once UnusedMember.Global
        public bool AtLeast(int n, IEnumerable<Literal> literals)
        {
            return Quantify(n, 0, literals);
        }
        #endregion
        
        #region Solver
        /// <summary>
        /// Try to find an assignment of truth values to propositions that satisfied the Program.
        /// Implements the WalkSAT algorithm
        /// </summary>
        /// <returns>True if a satisfying assignment was found.</returns>
        internal bool Solve()
        {
            for (var t = MaxTries; t > 0; t--)
            {
                MakeRandomAssignment();

                for (var f = MaxFlips; unsatisfiedClauses.Count > 0 && f > 0; f--)
                {
                    // Hill climb: pick an unsatisfied clause at random and flip one of its variables
#if KnuthWalkSAT
                    // This is the version of WalkSAT that appears in Knuth, but it doesn't match
                    // the version reported elsewhere, and it seems to be considerably slower.
                    var targetClause = Problem.Clauses[unsatisfiedClauses.RandomElement()];
                    var flipChoice = BestVariableToFlip(targetClause.Disjuncts);
                    if (flipChoice.Cost > 0 && Random.InRange(100) < 50)
                        // Flip a completely random variable
                        // This is to pull us out of local minima
                        Flip(Problem.FloatingVariables.RandomElement());
                    else
                        Flip(flipChoice.Variable);
#else
                    var targetClause = Problem.Clauses[unsatisfiedClauses.RandomElement()];
                    ushort flipChoice;
                    // This runs considerably faster than Knuth WalkSAT on my tests
                    if (Random.InRange(100) < RandomFlipProbability)
                        // Flip a completely random variable
                        // This is to pull us out of local minima
                        flipChoice = (ushort) Math.Abs(targetClause.Disjuncts.RandomElement());
                    else
                        // Hill climb: pick an unsatisfied clause at random and flip one of its variables;
                        flipChoice = BestVariableToFlip(targetClause.Disjuncts);
                    Flip(flipChoice);
#endif
                }

                if (unsatisfiedClauses.Count == 0)
                    return true;
            }
            
            // Give up
            return false;
        }

#if KnuthWalkSAT
        private struct FlipChoice
        {
            public readonly ushort Variable;
            public readonly int Cost;

            public FlipChoice(ushort variable, int cost)
            {
                Variable = variable;
                Cost = cost;
            }
        }
#endif

        /// <summary>
        /// Find the proposition from the specified clause that will do the least damage to the clauses that are already satisfied.
        /// </summary>
        /// <param name="disjuncts">Signed indices of the disjucts of the clause</param>
        /// <returns>Index of the prop to flip</returns>
#if KnuthWalkSAT
        private FlipChoice BestVariableToFlip(short[] disjuncts)
#else
        private ushort BestVariableToFlip(short[] disjuncts)
#endif
        {
            var bestCount = int.MaxValue;
            var best = 0;
#if RANDOMIZE
            // Walk disjuncts in a reasonably random order
            var dCount = (uint)disjuncts.Length;
            var index = Random.InRange(dCount);
            var prime = Random.Prime();
            for (var i = 0; i < dCount; i++)
            {
                var value = disjuncts[index];
                index = (index + prime) % dCount;
#else
                foreach (var value in disjuncts)
            {
#endif
                var threatCount = UnsatisfiedClauseDelta((ushort)Math.Abs(value));
                if (threatCount <= 0)
                    // Fast path - we've found an improvement; take it
                    // Real WalkSAT would continue searching for the best possible choice, but this
                    // gives better performance in my tests
                    // TODO - see if a faster way of computing ThreatenedClauseCount would improve things.
#if KnuthWalkSAT
                    return new FlipChoice((ushort) Math.Abs(value), 0);
#else
                    return (ushort) Math.Abs(value);
#endif
                if (threatCount < bestCount)
                {
                    best = value;
                    bestCount = threatCount;
                }
            }

#if KnuthWalkSAT
            return new FlipChoice((ushort)Math.Abs(best), bestCount);
#else
            return (ushort)Math.Abs(best);
#endif
        }

        /// <summary>
        /// The increase in the number of unsatisfied clauses as a result of flipping the specified variable
        /// </summary>
        /// <param name="pIndex">Index of the variable to consider flipping</param>
        /// <returns>The signed increase in the number of unsatisfied clauses</returns>
        int UnsatisfiedClauseDelta(ushort pIndex)
        {
            int threatCount = 0;
            var prop = Problem.Variables[pIndex];
            List<ushort> increasingClauses;
            List<ushort> decreasingClauses;

            if (propositions[pIndex])
            {
                // prop true -> false
                increasingClauses = prop.NegativeClauses;
                decreasingClauses = prop.PositiveClauses;
            }
            else
            {
                // prop true -> false
                increasingClauses = prop.PositiveClauses;
                decreasingClauses = prop.NegativeClauses;
            }

            foreach (var cIndex in increasingClauses)
            {
                // This clause is getting one more disjunct
                var clause = Problem.Clauses[cIndex];
                var count = trueDisjunctCount[cIndex];
                if (clause.OneTooFewDisjuncts(count))
                    threatCount--;
                else if (clause.OneTooManyDisjuncts((ushort) (count + 1)))
                    threatCount++;
            }

            foreach (var cIndex in decreasingClauses)
            {
                // This clause is getting one more disjunct
                var clause = Problem.Clauses[cIndex];
                var count = trueDisjunctCount[cIndex];
                if (clause.OneTooFewDisjuncts((ushort)(count-1)))
                    threatCount++;
                else if (clause.OneTooManyDisjuncts(count))
                    threatCount--;
            }

            

            //if (propositions[pIndex])
            //{
            //    // prop is currently true, so we would be flipping it to false

            //    // For positive literals the satisfied disjunct count would decrease
            //    foreach (var cIndex in prop.PositiveClauses)
            //    {
            //        if (Problem.Clauses[cIndex].OneTooFewDisjuncts((ushort)(trueDisjunctCount[cIndex] - 1)))
            //            threatCount++;
            //    }

            //    // For negative literals, the satisfied disjunct count would increase
            //    foreach (var cIndex in prop.NegativeClauses)
            //    {
            //        if (Problem.Clauses[cIndex].OneTooManyDisjuncts((ushort)(trueDisjunctCount[cIndex] + 1)))
            //            threatCount++;
            //    }
            //}
            //else
            //{
            //    // prop is currently false, so we would be flipping it to true

            //    // For positive literals the satisfied disjunct count would increase
            //    foreach (var cIndex in prop.PositiveClauses)
            //    {
            //        if (Problem.Clauses[cIndex].OneTooManyDisjuncts((ushort)(trueDisjunctCount[cIndex] + 1)))
            //            threatCount++;
            //    }

            //    // For negative literals, the satisfied disjunct count would decrease
            //    foreach (var cIndex in prop.NegativeClauses)
            //    {
            //        if (Problem.Clauses[cIndex].OneTooFewDisjuncts((ushort)(trueDisjunctCount[cIndex] - 1)))
            //            threatCount++;
            //    }
            //}

            return threatCount;
        }

        /// <summary>
        /// Flip the variable at the specified index.
        /// </summary>
        /// <param name="pIndex">Index of the variable/proposition to flip</param>
        private void Flip(ushort pIndex)
        {
            var prop = Problem.Variables[pIndex];
            if (prop.IsPredetermined)
                // Can't flip it.
                return;

            if (propositions[pIndex])
            {
                // Flip true -> false
                propositions[pIndex] = false;

                // Update the clauses in which this appears as a postive literal
                foreach (ushort cIndex in prop.PositiveClauses)
                {
                    // prop appears as a positive literal in clause.
                    // We just made it false, so clause now has fewer satisfied disjuncts.
                    var clause = Problem.Clauses[cIndex];
                    if (clause.OneTooManyDisjuncts(trueDisjunctCount[cIndex]))
                        // We just satisfied it
                        unsatisfiedClauses.Remove(cIndex);
                    var dCount = --trueDisjunctCount[cIndex];
                    if (clause.OneTooFewDisjuncts(dCount))
                        // It just transitioned from satisfied to unsatisfied
                        unsatisfiedClauses.Add(cIndex);
                }

                // Update the clauses in which this appears as a negative literal
                foreach (ushort cIndex in prop.NegativeClauses)
                {
                    // prop appears as a negative literal in clause.
                    // We just made it false, so clause now has more satisfied disjuncts.
                    var clause = Problem.Clauses[cIndex];
                    if (clause.OneTooFewDisjuncts(trueDisjunctCount[cIndex]))
                        // We just satisfied it
                        unsatisfiedClauses.Remove(cIndex);
                    var dCount = ++trueDisjunctCount[cIndex];
                    if (clause.OneTooManyDisjuncts(dCount))
                        // It just transitioned from satisfied to unsatisfied
                        unsatisfiedClauses.Add(cIndex);
                }
            }
            else
            {
                // Flip false -> true
                propositions[pIndex] = true;

                // Update the clauses in which this appears as a postive literal
                foreach (ushort cIndex in prop.PositiveClauses)
                {
                    // prop appears as a positive literal in clause.
                    // We just made it true, so clause now has more satisfied disjuncts.
                    var clause = Problem.Clauses[cIndex];
                    if (clause.OneTooFewDisjuncts(trueDisjunctCount[cIndex]))
                        // We just satisfied it
                        unsatisfiedClauses.Remove(cIndex);
                    var dCount = ++trueDisjunctCount[cIndex];
                    if (clause.OneTooManyDisjuncts(dCount))
                        // It just transitioned from satisfied to unsatisfied
                        unsatisfiedClauses.Add(cIndex);
                }

                // Update the clauses in which this appears as a negative literal
                foreach (ushort cIndex in prop.NegativeClauses)
                {
                    // prop appears as a negative literal in clause.
                    // We just made it true, so clause now has fewer satisfied disjuncts.
                    var clause = Problem.Clauses[cIndex];
                    if (clause.OneTooManyDisjuncts(trueDisjunctCount[cIndex]))
                        // We just satisfied it
                        unsatisfiedClauses.Remove(cIndex);
                    var dCount = --trueDisjunctCount[cIndex];
                    if (clause.OneTooFewDisjuncts(dCount))
                        // It just transitioned from satisfied to unsatisfied
                        unsatisfiedClauses.Add(cIndex);
                }
            }
        }

        /// <summary>
        /// Randomly assign values to the propositions,
        /// and initialize the other state information accordingly.
        /// </summary>
        private void MakeRandomAssignment()
        {
            // Initialize propositions[]
            for (var i = 0; i < propositions.Length; i++)
            {
                propositions[i] = Problem.Variables[i].IsPredetermined?Problem.Variables[i].PredeterminedValue:Random.Next() % 2 == 0;
            }

            unsatisfiedClauses.Clear();

            // Initialize trueDisjunctCount[] and unsatisfiedClauses
            for (ushort i = 0; i < trueDisjunctCount.Length; i++)
            {
                var c = Problem.Clauses[i];
                var satisfiedDisjuncts = c.CountDisjuncts(this);
                trueDisjunctCount[i] = satisfiedDisjuncts;
                if (!c.IsSatisfied(satisfiedDisjuncts))
                    unsatisfiedClauses.Add(i);
            }
        }
#endregion
    }
}
