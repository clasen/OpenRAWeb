# Core Software Development Principles

---

## 1. Apply the DRY Principle (Don't Repeat Yourself)

Every piece of knowledge must have a single, authoritative, unambiguous representation within the system. Logic duplication is the root cause of fragility and maintenance nightmares.

---

## 2. Follow the One Thing Rule

A function or method must do one thing and do it well. If a function mixes low-level operations with high-level policy, it must be decomposed into smaller, focused units.

---

## 3. Maintain Orthogonality (Decoupling)

Design components to be independent and single-purpose; changes in one module must not affect unrelated ones. Follow the Law of Demeter: an object should not know the internals of the objects it manipulates.

---

## 4. Make It Work, Then Make It Right

Focus first on achieving functionality. Once working, dedicate mental energy to cleaning up (refactoring) the resulting code. Never declare work done just because the code works — you must also make it *right*.

---

## 5. Crash Early

Code must be defensive. If it detects an impossible state or a contract violation, it must terminate execution immediately to prevent data corruption.

---

## 6. Adopt a Ubiquitous Language

Use business domain terms — provided by domain experts — in class and method names, so the code reads as a clear narrative of the business logic.

---

## 7. Apply the Boy Scout Rule

Always leave the code a little cleaner than you found it. This prevents software entropy and the accumulation of "broken windows" (neglected, decaying code).

---

## 8. Avoid Commented-Out Code and Debug Logs

Commented-out code is an abomination and must be deleted, not committed to the repository. Change history belongs in version control — not in comments or inline diaries inside the code.
