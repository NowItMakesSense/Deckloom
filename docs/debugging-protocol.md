# Systematic debugging protocol

Use this whenever a test fails unexpectedly, a bug is reported, or behavior doesn't match the spec. Never patch code you have not understood.

1. **Reproduce.** Get a minimal, reliable repro (a failing test is ideal - write one if none exists). If you can't reproduce it, you don't understand it yet - keep investigating, don't guess a fix.
2. **Understand the root cause.** Trace the failure to its actual origin (not just where it surfaces). Read the relevant code/logs/stack trace fully before forming a hypothesis. Do not fix the first plausible-looking line you find.
3. **Form and test a minimal hypothesis.** State what you believe is wrong and why, then verify it narrowly (a log, a focused test, stepping through) before writing the real fix. If the hypothesis is wrong, go back to step 2 - don't layer on a second guess.
4. **Fix and add a regression test.** Apply the minimal fix for the confirmed root cause, then add/extend a test that would have caught this, so it can't regress silently. Run the full suite, not just the new test, before reporting done.

Never: patch symptoms without understanding the cause, fix multiple suspected causes at once ("shotgun debugging"), or suppress/ignore a failing test instead of fixing or deleting it with justification.
