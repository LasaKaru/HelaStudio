package dev.shellwright.shell

import android.app.Application

/**
 * The application object.
 *
 * ⚠️ Deliberately empty. Anything expensive here delays every cold start, and
 * `01_ENGINEERING_STANDARDS.md` §10 bans work in `onCreate` for exactly that
 * reason. When plugins arrive in Sprint 10 they register lazily on first bridge
 * call — fifteen plugins initialising here would be a two-second cold start.
 */
public class ShellApplication : Application()
