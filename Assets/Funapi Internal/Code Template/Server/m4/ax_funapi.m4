# Copyright (C) 2012 Nexon Korea Corporation All Rights Reserved.
#
# This work is confidential and proprietary to Nexon Korea Corporation and
# must not be used, disclosed, copied, or distributed without the prior
# consent of Nexon Korea Corporation.
#
# SYNOPSIS
#
#   AX_FUNAPI([MINIMUM-VERSION], [ACTION-IF-FOUND], [ACTION-IF-NOT-FOUND])
#
# DESCRIPTION
#
#   Test for the Nexon FunAPI library of a particular version (or newer)
#
#   If no path to the installed library is given the macro searchs
#   under /usr, /usr/local, /opt and /opt/local.
#
#   This macro calls:
#
#    AC_SUBST(FUNAPI_ROOT)
#    AC_SUBST(FUNAPI_BINDIR)
#    AC_SUBST(FUNAPI_DATADIR)
#    AC_SUBST(FUNAPI_INCLUDEDIR)
#    AC_SUBST(FUNAPI_LIBDIR)
#    AC_SUBST(FUNAPI_CPPFLAGS)
#    AC_SUBST(FUNAPI_LDFLAGS)
#    AC_SUBST(FUNAPI_LIBS)
#    AC_SUBST(FUNAPI_LD_PRELOAD)
#
#   And sets:
#
#     HAVE_FUNAPI
#

AC_DEFUN([AX_FUNAPI],
[
AC_ARG_WITH([funapi],
  [AS_HELP_STRING([--with-funapi@<:@=ARG@:>@],
    [use Nexon FunAPI library from a standard location (ARG=yes),
     from the specified location (ARG=<path>),
     or disable it (ARG=no)
     @<:@ARG=yes@:>@ ])],
    [if test "$withval" = "no"; then
       want_funapi="no"
     elif test "$withval" = "yes"; then
       want_funapi="yes"
       ac_funapi_path=""
     else
       want_funapi="yes"
       ac_funapi_path="$withval"
     fi],
    [want_funapi="yes"])

if test "x$want_funapi" = "xyes"; then
  funapi_lib_version=[$1]
  funapi_lib_version_shorten=`expr $funapi_lib_version : '\([[0-9]]*\.[[0-9]]*\)'`
  funapi_lib_version_major=`expr $funapi_lib_version : '\([[0-9]]*\)'`
  funapi_lib_version_minor=`expr $funapi_lib_version : '[[0-9]]*\.\([[0-9]]*\)'`
  funapi_lib_version_sub_minor=`expr $funapi_lib_version : '[[0-9]]*\.[[0-9]]*\.\([[0-9]]*\)'`
  if test "x$funapi_lib_version_sub_minor" = "x"; then
    funapi_lib_version_sub_minor="0"
    fi
  WANT_FUNAPI_VERSION=`expr $funapi_lib_version_major \* 10000 \+  $funapi_lib_version_minor \* 100 \+ $funapi_lib_version_sub_minor`
  AC_MSG_CHECKING(for FunAPI library >= $funapi_lib_version)
  succeeded=no

  dnl first we check the system location for the library.
  if test "$ac_funapi_path" != ""; then
    funapi_root_candidates="$ac_funapi_path"
  else
    funapi_root_candidates="/usr /usr/local /opt /opt/local"
  fi

  for ac_funapi_path_tmp in $funapi_root_candidates; do
    if test -d "$ac_funapi_path_tmp/bin/funapi" && test -r "$ac_funapi_path_tmp/bin/funapi"; then
      if test -d "$ac_funapi_path_tmp/share/funapi" && test -r "$ac_funapi_path_tmp/share/funapi"; then
        if test -d "$ac_funapi_path_tmp/include/funapi" && test -r "$ac_funapi_path_tmp/include/funapi"; then
          if test -d "$ac_funapi_path_tmp/lib/funapi" && test -r "$ac_funapi_path_tmp/lib/funapi"; then
            FUNAPI_ROOT="$ac_funapi_path_tmp"
            break
          fi
        fi
      fi
    fi
  done

  if test x"$FUNAPI_ROOT" = "x"; then
    AC_MSG_NOTICE([[We could not detect the FunAPI library.]])
    # if not found
    ifelse([$3], , :, [$3])
  fi

  FUNAPI_BINDIR="$FUNAPI_ROOT/bin/funapi"
  FUNAPI_DATADIR="$FUNAPI_ROOT/share/funapi"
  FUNAPI_LIBDIR="$FUNAPI_ROOT/lib/funapi"
  FUNAPI_INCLUDEDIR="$FUNAPI_ROOT/include"

  FUNAPI_CPPFLAGS="-I$FUNAPI_INCLUDEDIR"
  FUNAPI_LDFLAGS="-L$FUNAPI_LIBDIR"

  FUNAPI_LIBS=
  FUNAPI_LD_PRELOAD=
  for ac_funapi_lib_tmp in `ls $FUNAPI_LIBDIR/*.so`; do
    nm --dynamic `readlink -f $ac_funapi_lib_tmp` | grep -q main > /dev/null 2> /dev/null
    if test $? -eq 1; then
      ac_funapi_lib_basename_tmp=`basename $ac_funapi_lib_tmp`
      FUNAPI_LD_PRELOAD="$FUNAPI_LD_PRELOAD:$ac_funapi_lib_basename_tmp"
      ac_funapi_lib_canonical_tmp=`echo $ac_funapi_lib_basename_tmp | sed -e 's,\(lib\)\(.*\)\(.so\),-l\2,g'`
      FUNAPI_LIBS="$FUNAPI_LIBS $ac_funapi_lib_canonical_tmp"
    fi
  done

  CPPFLAGS_SAVED="$CPPFLAGS"
  CPPFLAGS="$CPPFLAGS $FUNAPI_CPPFLAGS"
  export CPPFLAGS

  LDFLAGS_SAVED="$LDFLAGS"
  LDFLAGS="$LDFLAGS $FUNAPI_LDFLAGS"
  export LDFLAGS

  AC_REQUIRE([AC_PROG_CXX])
  AC_LANG_PUSH(C++)
  AC_COMPILE_IFELSE(
    [AC_LANG_PROGRAM([[
      @%:@include <funapi/version.h>
    ]], [[
      #if FUNAPI_VERSION >= $WANT_FUNAPI_VERSION
        // OK.
      #else
      #  error old FunAPI
      #endif
    ]])],[
      succeeded=yes
      found_system=yes
    ],[])
  AC_LANG_POP([C++])

  if test "$succeeded" != "yes"; then
    AC_MSG_NOTICE([[We could not detect the FunAPI library.]])
    # if not found
    ifelse([$3], , :, [$3])
  else
    AC_SUBST(FUNAPI_ROOT)
    AC_SUBST(FUNAPI_BINDIR)
    AC_SUBST(FUNAPI_DATADIR)
    AC_SUBST(FUNAPI_INCLUDEDIR)
    AC_SUBST(FUNAPI_LIBDIR)
    AC_SUBST(FUNAPI_CPPFLAGS)
    AC_SUBST(FUNAPI_LDFLAGS)
    AC_SUBST(FUNAPI_LIBS)
    AC_SUBST(FUNAPI_LD_PRELOAD)
    AC_DEFINE(HAVE_FUNAPI,,[define if the FunAPI library is available])
    # if found
    ifelse([$2], , :, [$2])
  fi

  CPPFLAGS="$CPPFLAGS_SAVED"
  LDFLAGS="$LDFLAGS_SAVED"
fi

])

