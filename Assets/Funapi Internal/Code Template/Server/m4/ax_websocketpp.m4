# Copyright (C) 2012 Nexon Korea Corporation All Rights Reserved.
#
# This work is confidential and proprietary to Nexon Korea Corporation and
# must not be used, disclosed, copied, or distributed without the prior
# consent of Nexon Korea Corporation.
#
# SYNOPSIS
#
#   AX_WEBSOCKETPP
#
# DESCRIPTION
#
#   Test for the Websocketpp library of a particular version (or newer)
#
#   If no path to the installed library is given the macro searchs
#   under /usr, /usr/local, /opt and /opt/local.
#
#   This macro calls:
#
#     AC_SUBST(WEBSOCKETPP_CPPFLAGS)
#     AC_SUBST(WEBSOCKETPP_LDFLAGS)
#     AC_SUBST(WEBSOCKETPP_LIBS)
#
#   And sets:
#
#     HAVE_WEBSOCKETPP
#

AC_DEFUN([AX_WEBSOCKETPP],
[
AC_ARG_WITH([websocketpp],
  [AS_HELP_STRING([--with-websocketpp@<:@=ARG@:>@],
    [use Websocketpp library from a standard location (ARG=yes),
     from the specified location (ARG=<path>),
     or disable it (ARG=no)
     @<:@ARG=yes@:>@ ])],
    [if test "$withval" = "no"; then
       want_websocketpp="no"
     elif test "$withval" = "yes"; then
       want_websocketpp="yes"
       ac_websocketpp_path=""
     else
       want_websocketpp="yes"
       ac_websocketpp_path="$withval"
     fi],
    [want_websocketpp="yes"])

if test "x$want_websocketpp" = "xyes"; then
  succeeded=no

  dnl first we check the system location for the library.
  if test "$ac_websocketpp_path" != ""; then
    websocketpp_root_candidates="$ac_websocketpp_path"
  else
    websocketpp_root_candidates="/usr /usr/local /opt /opt/local"
  fi

  for ac_websocketpp_path_tmp in $websocketpp_root_candidates; do
    if test -d "$ac_websocketpp_path_tmp/include/websocketpp" && test -r "$ac_websocketpp_path_tmp/include/websocketpp"; then
      if test -f "$ac_websocketpp_path_tmp/lib/libwebsocketpp.so" && test -r "$ac_websocketpp_path_tmp/lib/libwebsocketpp.so"; then
        WEBSOCKETPP_ROOT="$ac_websocketpp_path_tmp"
      fi
    fi
  done

  if test x"$WEBSOCKETPP_ROOT" = "x"; then
    AC_MSG_ERROR([[We could not detect the Websocketpp library.]])
  fi

  WEBSOCKETPP_LIBDIR="$WEBSOCKETPP_ROOT/lib"
  WEBSOCKETPP_INCLUDEDIR="$WEBSOCKETPP_ROOT/include"

  WEBSOCKETPP_CPPFLAGS="-I$WEBSOCKETPP_INCLUDEDIR"
  WEBSOCKETPP_LDFLAGS="-L$WEBSOCKETPP_LIBDIR"
  WEBSOCKETPP_LIBS='-lwebsocketpp'

  CPPFLAGS_SAVED="$CPPFLAGS"
  CPPFLAGS="$CPPFLAGS $WEBSOCKETPP_CPPFLAGS"
  export CPPFLAGS

  LDFLAGS_SAVED="$LDFLAGS"
  LDFLAGS="$LDFLAGS $WEBSOCKETPP_LDFLAGS"
  export LDFLAGS

  AC_REQUIRE([AC_PROG_CXX])
  AC_LANG_PUSH(C++)
  AC_COMPILE_IFELSE(
    [AC_LANG_PROGRAM([[
      @%:@include <websocketpp/common.hpp>
    ]], [[
    ]])],[
      succeeded=yes
      found_system=yes
    ],[])
  AC_LANG_POP([C++])

  if test "$succeeded" != "yes"; then
    AC_MSG_ERROR([[We could not detect the Websocketpp library..]])
  else
    AC_SUBST(WEBSOCKETPP_CPPFLAGS)
    AC_SUBST(WEBSOCKETPP_INCLUDEDIR)
    AC_SUBST(WEBSOCKETPP_LDFLAGS)
    AC_SUBST(WEBSOCKETPP_LIBDIR)
    AC_SUBST(WEBSOCKETPP_LIBS)
    AC_DEFINE(HAVE_WEBSOCKETPP,,[define if the Websocketpp library is available])
  fi

  CPPFLAGS="$CPPFLAGS_SAVED"
  LDFLAGS="$LDFLAGS_SAVED"
fi

])

