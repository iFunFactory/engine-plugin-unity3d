AC_DEFUN([AX_CHECK_DEBUGGING], [
AC_ARG_ENABLE(
  [debugging],
  [AC_HELP_STRING([--disable-debugging],
                  [Build with -DNDEBUG])],
  [case "${enableval}" in # (
     yes) debugging=true ;; # (
     no)  debugging=false ;; # (
     *) AC_MSG_ERROR([bad value ${enableval} for --enable-debugging]) ;;
   esac],
  [debugging=true])
AM_CONDITIONAL([DEBUGGING], [test x$debugging = xtrue])
])

