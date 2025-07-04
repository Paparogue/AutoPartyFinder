﻿using System;

namespace AutoPartyFinder.Delegates;

public delegate long StartRecruitingDelegate(IntPtr agent);
public delegate void OpenRecruitmentWindowDelegate(IntPtr agent, byte isUpdate, byte skipRestoreState);
public delegate void LeaveDutyDelegate(IntPtr agent, byte skipCrossRealmCheck);
public delegate long RefreshListingsDelegate(IntPtr agent, IntPtr atkValue);
public delegate byte IsLocalPlayerPartyLeaderDelegate();
public delegate byte IsLocalPlayerInPartyDelegate();
public delegate ulong GetActiveRecruiterContentIdDelegate(IntPtr agent);
public delegate byte CrossRealmGetPartyMemberCountDelegate();